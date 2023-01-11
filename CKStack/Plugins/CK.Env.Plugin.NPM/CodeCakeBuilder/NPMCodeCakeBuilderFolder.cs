using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.NPM;

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public class NPMCodeCakeBuilderFolder : PluginFolderBase
    {
        readonly RepositoryXmlFile _repositoryXml;
        readonly NodeSolutionDriver _nodeDriver;
        readonly SolutionDriver _driver;

        public NPMCodeCakeBuilderFolder( GitRepository f,
                                         // When this will not be used (migration from NPMSolution.xml done),
                                         // The package dependency to CK.Env.Basics can be removed.
                                         RepositoryXmlFile repositoryXml,
                                         NodeSolutionDriver nodeDriver,
                                         SolutionDriver driver,
                                         NormalizedPath branchPath )
            : base( f, branchPath, "CodeCakeBuilder", "NPM/Res" )
        {
            _repositoryXml = repositoryXml;
            _nodeDriver = nodeDriver;
            _driver = driver;
        }

        /// <summary>
        /// Gets the name of this command: it is "<see cref="FolderPath"/>(NPM)".
        /// </summary>
        /// <returns>The command name.</returns>
        protected override NormalizedPath GetCommandProviderName() => FolderPath.AppendPart( "(NPM)" );

        protected override void DoApplySettings( IActivityMonitor monitor )
        {
            if( !_nodeDriver.TryGetHasNodeSolution( monitor, out var hasNodeSolution ) ) return;

            // Delete all "yarn".
            DeleteFileOrFolder( monitor, "yarn" );

            // Temporary until we throw away AdaptBuildNPMArtifactForPushFeeds
            // since CCB will rely on the RepositoryInfo.xml file.
            var solution = _nodeDriver.SolutionDriver.GetSolution( monitor, false );
            Debug.Assert( solution != null );

            // Temporary.
            if( !hasNodeSolution )
            {
                var old = FolderPath.AppendPart( "NPMSolution.xml" );
                var oldNPMSolutionfile = GitFolder.FileSystem.GetFileInfo( old );
                if( oldNPMSolutionfile.Exists )
                {
                    monitor.Info( $"Migrating NPMSolution to RepositoryInfo.xml" );
                    Throw.CheckState( !oldNPMSolutionfile.IsDirectory );
                    var r = oldNPMSolutionfile.ReadAsXDocument().Root!;
                    var nodeComment = new XComment( "NodeProject must have a Path attribute and an optional OutputPath (where the 'npm pack' will be executed) if it differs from the Path." + Environment.NewLine
                                                    + "AngularWorkspace and YarnWorkspace must have only a Path attribute." );
                    var nodeSolution = new XElement( "NodeSolution",
                                                        r.Elements( "Project" )
                                                        .Where( p => p.HasAttributes && !string.IsNullOrEmpty( p.Attribute( "Path" )?.Value ) )
                                                        .Select( p => new XElement( "NodeProject",
                                                                                    new XAttribute( p.Attribute( "Path" )! ),
                                                                                    !string.IsNullOrEmpty( p.Attribute( "OutputFolder" )?.Value )
                                                                                    && p.Attribute( "Path" )?.Value != p.Attribute( "OutputFolder" )?.Value
                                                                                        ? new XAttribute( "OutputPath", p.Attribute( "OutputFolder" )!.Value )
                                                                                        : null ) ),
                                                      r.Elements( "AngularWorkspace" )
                                                       .Where( p => p.HasAttributes && !string.IsNullOrEmpty( p.Attribute( "Path" )?.Value ) )
                                                       .Select( e => new XElement( e ) ) );
                    _repositoryXml.EnsureDocument().Root!.Add( nodeComment, nodeSolution );
                    _repositoryXml.Save( monitor );
                    GitFolder.FileSystem.Delete( monitor, old );
                    _nodeDriver.SetDirty( monitor );
                    hasNodeSolution = true;
                }
            }

            if( hasNodeSolution )
            {
                //CakeExtensions
                SetTextResource( monitor, "CakeExtensions/NpmDistTagRunner.cs" );
                SetTextResource( monitor, "CakeExtensions/NpmView.cs" );
                SetTextResource( monitor, "CakeExtensions/NpmGetNpmVersion.cs" );
                //npm itself
                SetTextResource( monitor, "npm/Build.NPMArtifactType.cs", text => AdaptBuildNPMArtifactForPushFeeds( text, solution ) );
                SetTextResource( monitor, "npm/Build.NPMFeed.cs" );
                SetTextResource( monitor, "npm/NPMProject.cs" );
                SetTextResource( monitor, "npm/NPMPublishedProject.cs" );
                SetTextResource( monitor, "npm/NPMSolution.cs" );
                SetTextResource( monitor, "npm/NPMProjectContainer.cs" );
                SetTextResource( monitor, "npm/TempFileTextModification.cs" );
                SetTextResource( monitor, "npm/SimplePackageJsonFile.cs" );
                SetTextResource( monitor, "npm/AngularWorkspace.cs" );
            }
            else
            {
                DeleteFileOrFolder( monitor, "CakeExtensions/NpmDistTagRunner.cs" );
                DeleteFileOrFolder( monitor, "CakeExtensions/NpmView.cs" );
                DeleteFileOrFolder( monitor, "CakeExtensions/NpmGetNpmVersion.cs" );
                DeleteFileOrFolder( monitor, "npm" );
            }

        }

        string AdaptBuildNPMArtifactForPushFeeds( string text, ISolution s )
        {
            Match m = Regex.Match( text, @"protected\s*override\s*IEnumerable<ArtifactFeed>*\sGetRemoteFeeds()[^{]*\{((?>{(?<opening>)|[^{}]|}(?<-opening>))*(?(opening)(?!)))\}" );
            if( !m.Success )
            {
                throw new Exception( "Expected pattern yield return new AzureNPMFeed( this, ...); in Build.NPMArtifactType.cs." );
            }
            StringBuilder b = new StringBuilder();
            bool atLeastOne = false;
            foreach( var r in s.ArtifactTargets.OfType<INPMRepository>() )
            {
                atLeastOne = true;
                if( r.QualityFilter.HasMin || r.QualityFilter.HasMax )
                {
                    b.Append( "if( " );
                    if( r.QualityFilter.HasMin )
                    {
                        b.Append( "GlobalInfo.BuildInfo.Version.PackageQuality >= CSemVer.PackageQuality." )
                         .Append( r.QualityFilter.Min.ToString() )
                         .Append( ' ' );
                    }
                    if( r.QualityFilter.HasMax )
                    {
                        if( r.QualityFilter.HasMin ) b.Append( "&& " );
                        b.Append( "GlobalInfo.BuildInfo.Version.PackageQuality <= CSemVer.PackageQuality." )
                         .Append( r.QualityFilter.Max.ToString() )
                         .Append( ' ' );
                    }
                    b.Append( ") " );
                }
                switch( r )
                {
                    case INPMAzureRepository a:
                        b.Append( "yield return new AzureNPMFeed( this, \"" )
                            .Append( a.Organization ).Append( "\", " )
                            .Append( $"\"{a.FeedName}\"" ).Append( ", " )
                            .Append( a.ProjectName != null ? $"\"{a.ProjectName}\"" : "null" )
                            .AppendLine( " );" );
                        break;
                    case INPMStandardRepository n:
                        Uri uri = new Uri( n.Url );
                        if( uri.IsFile )
                        {
                            b.Append( "yield return new NPMLocalFeed( this, \"" )
                            .Append( uri.AbsolutePath )
                            .AppendLine( "\" );" );
                        }
                        else
                        {
                            b.Append( "yield return new NPMRemoteFeed( this, \"" )
                           .Append( n.SecretKeyName )
                           .Append( "\", \"" )
                           .Append( n.Url )
                           .Append( "\", " )
                           .Append( n.UsePassword ? "true" : "false" )
                           .AppendLine( " );" );
                        }

                        break;
                }
            }
            if( !atLeastOne ) b.AppendLine().Append( "yield break;" );
            text = text.Replace( m.Groups[2].Value, b.ToString() );
            return text;
        }

    }
}
