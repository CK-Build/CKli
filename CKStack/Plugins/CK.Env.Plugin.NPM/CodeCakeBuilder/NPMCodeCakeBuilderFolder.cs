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

        protected override void DoApplySettings( IActivityMonitor m )
        {
            if( !_nodeDriver.TryGetHasNodeSolution( m, out var hasNodeSolution ) ) return;

            // Delete all "yarn".
            DeleteFileOrFolder( m, "yarn" );

            // Temporary until we throw away AdaptBuildNPMArtifactForPushFeeds
            // since CCB will rely on the RepositoryInfo.xml file.
            var solution = _nodeDriver.SolutionDriver.GetSolution( m, false );
            Debug.Assert( solution != null );

            // Temporary.
            if( !hasNodeSolution )
            {
                var old = FolderPath.AppendPart( "NPMSolution.xml" );
                var oldNPMSolutionfile = GitFolder.FileSystem.GetFileInfo( old );
                if( oldNPMSolutionfile.Exists )
                {
                    m.Info( $"Migrating NPMSolution to RepositoryInfo.xml" );
                    Throw.CheckState( !oldNPMSolutionfile.IsDirectory );
                    var r = oldNPMSolutionfile.ReadAsXDocument().Root!;
                    _repositoryXml.Document.Root!.Add( r.Elements( "Project" )
                                                        .Where( p => p.HasAttributes && !string.IsNullOrEmpty( p.Attribute( "Path" )?.Value ) )
                                                        .Select( p => new XElement( "NodeProject",
                                                                                    new XAttribute( p.Attribute( "Path" ) ),
                                                                                    !string.IsNullOrEmpty( p.Attribute( "OutputFolder" )?.Value )
                                                                                        ? new XAttribute( "OutputPath", p.Attribute( "OutputFolder" ).Value )
                                                                                        : null ) ),
                                                      r.Elements( "AngularWorkspace" )
                                                       .Where( p => p.HasAttributes && !string.IsNullOrEmpty( p.Attribute( "Path" )?.Value ) )
                                                       .Select( e => new XElement( e ) )
                                                     );
                    _repositoryXml.Save( m );
                    GitFolder.FileSystem.Delete( m, old );
                    hasNodeSolution = true;
                }
            }

            if( hasNodeSolution )
            {
                //CakeExtensions
                SetTextResource( m, "CakeExtensions/NpmDistTagRunner.cs" );
                SetTextResource( m, "CakeExtensions/NpmView.cs" );
                SetTextResource( m, "CakeExtensions/NpmGetNpmVersion.cs" );
                //npm itself
                SetTextResource( m, "npm/Build.NPMArtifactType.cs", text => AdaptBuildNPMArtifactForPushFeeds( text, solution ) );
                SetTextResource( m, "npm/Build.NPMFeed.cs" );
                SetTextResource( m, "npm/NPMProject.cs" );
                SetTextResource( m, "npm/NPMPublishedProject.cs" );
                SetTextResource( m, "npm/NPMSolution.cs" );
                SetTextResource( m, "npm/NPMProjectContainer.cs" );
                SetTextResource( m, "npm/TempFileTextModification.cs" );
                SetTextResource( m, "npm/SimplePackageJsonFile.cs" );
                SetTextResource( m, "npm/AngularWorkspace.cs" );
            }
            else
            {
                DeleteFileOrFolder( m, "CakeExtensions/NpmDistTagRunner.cs" );
                DeleteFileOrFolder( m, "CakeExtensions/NpmView.cs" );
                DeleteFileOrFolder( m, "CakeExtensions/NpmGetNpmVersion.cs" );
                DeleteFileOrFolder( m, "npm" );
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
