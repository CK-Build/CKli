using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.NPM;
using CK.Text;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.Env.Plugin
{
    public class NPMCodeCakeBuilderFolder : PluginFolderBase
    {
        readonly NPMProjectsDriver _npmDriver;
        readonly SolutionDriver _driver;

        public NPMCodeCakeBuilderFolder( GitFolder f, NPMProjectsDriver npmDriver, SolutionDriver driver, NormalizedPath branchPath )
            : base( f, branchPath, "CodeCakeBuilder", "NPM/Res" )
        {
            _npmDriver = npmDriver;
            _driver = driver;
        }

        /// <summary>
        /// Gets the name of this command: it is "<see cref="FolderPath"/>(NPM)".
        /// </summary>
        /// <returns>The command name.</returns>
        protected override NormalizedPath GetCommandProviderName() => FolderPath.AppendPart( "(NPM)" );

        protected override void DoApplySettings( IActivityMonitor m )
        {
            var solution = _driver.GetSolution( m, allowInvalidSolution: true );
            if( solution == null ) return;

            var projects = _npmDriver.GetNPMProjects( m );
            if( projects == null ) return;
            bool useNpm = projects.Any();
            if( useNpm )
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
                DeleteFile( m, "CakeExtensions/NpmDistTagRunner.cs" );
                DeleteFile( m, "CakeExtensions/NpmView.cs" );
                DeleteFile( m, "CakeExtensions/NpmGetNpmVersion.cs" );
                DeleteFile( m, "npm/Build.NPMArtifactType.cs" );
                DeleteFile( m, "npm/Build.NPMFeed.cs" );
                DeleteFile( m, "npm/NPMProject.cs" );
                DeleteFile( m, "npm/NPMPublishedProject.cs" );
                DeleteFile( m, "npm/NPMSolution.cs" );
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
                        b.Append( "GlobalInfo.Version.PackageQuality >= CSemVer.PackageQuality." )
                         .Append( r.QualityFilter.Min.ToString() )
                         .Append( ' ' );
                    }
                    if( r.QualityFilter.HasMax )
                    {
                        if( r.QualityFilter.HasMin ) b.Append( "&& " );
                        b.Append( "GlobalInfo.Version.PackageQuality <= CSemVer.PackageQuality." )
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

        string AdaptBuild( string text )
        {
            var name = GitFolder.SubPath.LastPart;
            Regex r = new Regex(
                  "(?<1>const\\s+string\\s+solutionName\\s*=\\s*\").*?(?<2>\";\\s*//\\s*!Transformable)",
                  RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant );
            return r.Replace( text, "$1" + name + "$2" );
        }
    }
}
