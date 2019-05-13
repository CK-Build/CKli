using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.NPM;
using CK.NuGetClient;
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
            : base( f, branchPath, "CodecakeBuilder" )
        {
            _npmDriver = npmDriver;
            _driver = driver;
        }

        protected override void DoApplySettings( IActivityMonitor m )
        {
            var solution = _driver.GetSolution( m );
            if( solution == null ) return;
            var projects = _npmDriver.GetNPMProjects( m );
            if( projects == null ) return;
            bool useNpm = projects.Any();
            if( useNpm )
            {
                //CakeExtensions
                SetTextResource( m, "CakeExtensions/NpmDistTagRunner.cs" );
                SetTextResource( m, "CakeExtensions/NpmView.cs" );
                //npm itself
                SetTextResource( m, "npm/Build.NPMArtifactType.cs", text => AdaptBuildNPMArtifactForPushFeeds( text, solution ) );
                SetTextResource( m, "npm/Build.NPMFeed.cs" );
                SetTextResource( m, "npm/NPMProject.cs" );
                SetTextResource( m, "npm/NPMPublishedProject.cs" );
                SetTextResource( m, "npm/NPMSolution.cs" );
            }
            else
            {
                m.Info( "Removing build files related to NPM handling." );
                DeleteFile( m, "CakeExtensions/NpmDistTagRunner.cs" );
                DeleteFile( m, "CakeExtensions/NpmView.cs" );
                DeleteFile( m, "npm/Build.NPMArtifactType.cs" );
                DeleteFile( m, "npm/Build.NPMFeed.cs" );
                DeleteFile( m, "npm/NPMProject.cs" );
                DeleteFile( m, "npm/NPMPublishedProject.cs" );
                DeleteFile( m, "npm/NPMSolution.cs" );
            }

        }

        string AdaptBuildNPMArtifactForPushFeeds( string text, ISolution s )
        {
            Match m = Regex.Match( text, @"return new NPMRemoteFeedBase\[\]{.*?};", RegexOptions.Singleline | RegexOptions.CultureInvariant );
            if( !m.Success )
            {
                throw new Exception( "Expected pattern return new NPMRemoteFeedBase[]{...} in Build.NPMArtifactType.cs." );
            }
            StringBuilder b = new StringBuilder();
            b.AppendLine( "return new NPMRemoteFeedBase[]{" );
            bool atLeastOne = false;
            foreach( var info in s.ArtifactTargets.Select( a => a.Info ).OfType<INPMFeedInfo>() )
            {
                b.AppendLine( atLeastOne ? "," : "" );
                atLeastOne = true;
                switch( info )
                {
                    case NPMAzureFeedInfo a:
                        b.Append( "new AzureNPMFeed( this, \"" )
                            .Append( a.Organization ).Append( "\", \"" )
                            .Append( a.FeedName ).Append( "\" )" );
                        break;
                    case NPMStandardFeedInfo n:
                        b.Append( "new NPMRemoteFeed( this, " )
                            .Append( n.SecretKeyName )
                            .Append( "\", \"" )
                            .Append( n.Url )
                            .Append( "\", " )
                            .Append( n.UsePassword )
                            .Append( " )" );
                        break;
                }
            }
            b.AppendLine().Append( "};" );
            text = text.Replace( m.Value, b.ToString() );
            return text;
        }

        string AdaptBuild( string text )
        {
            var name = Folder.SubPath.LastPart;
            Regex r = new Regex(
                  "(?<1>const\\s+string\\s+solutionName\\s*=\\s*\").*?(?<2>\";\\s*//\\s*!Transformable)",
                  RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant );
            return r.Replace( text, "$1" + name + "$2" );
        }
    }
}
