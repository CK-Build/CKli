using CK.Core;
using CK.NuGetClient;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.Plugins.SolutionFiles
{
    public class CodeCakeBuilderFolder : PluginFolderBase
    {
        readonly SolutionDriver _driver;
        readonly ISolutionSettings _settings;

        public CodeCakeBuilderFolder( GitFolder f, SolutionDriver driver, ISolutionSettings settings, NormalizedPath branchPath )
            : base( f, branchPath, "CodecakeBuilder" )
        {
            _driver = driver;
            _settings = settings;
        }

        protected override void DoApplySettings( IActivityMonitor m )
        {
            SetTextResource( m, "InstallCredentialProvider.ps1" );
            SetTextResource( m, "Program.cs" );
            UpdateTextResource( m, "Build.cs", AdaptBuild );
            SetTextResource( m, "Build.NuGetHelper.cs" );
            SetTextResource( m, "Build.StandardCheckRepository.cs", AdaptStandardCheckRepositoryForPushFeeds );
            SetTextResource( m, "Build.StandardSolutionBuild.cs" );
            if( _settings.NoUnitTests )
            {
                m.Info( "Removing Build.StandardUnitTests since NoUnitTests is true." );
                DeleteFile( m, "Build.StandardUnitTests.cs" );
            }
            else
            {
                SetTextResource( m, "Build.StandardUnitTests.cs" );
            }
            SetTextResource( m, "Build.StandardCreateNuGetPackages.cs" );
            SetTextResource( m, "Build.StandardPushNuGetPackages.cs" );

            bool? produceCKSetupComponents = _driver.AreCKSetupComponentsProduced( m );
            if( produceCKSetupComponents == null ) return;

            if( produceCKSetupComponents == true )
            {
                m.Info( "Adding Build.StandardPushCKSetupComponents.cs since CKSetup components are produced." );
                SetTextResource( m, "Build.StandardPushCKSetupComponents.cs", text => AdaptStandardStandardPushCKSetupComponents( m, text ) );
            }
            else
            {
                DeleteFile( m, "Build.StandardPushCKSetupComponents.cs" );
            }
        }

        string AdaptStandardStandardPushCKSetupComponents( IActivityMonitor monitor, string text )
        {
            Match m = Regex.Match( text, @"return new CKSetupComponent\[\]{.*?};", RegexOptions.Singleline | RegexOptions.CultureInvariant );
            if( !m.Success )
            {
                throw new Exception( "Expected pattern return new CKSetupComponent[]{...} in Build.StandardPushCKSetupComponents.cs." );
            }
            var allSolutions = _driver.GetAllSolutions( monitor );
            if( allSolutions == null ) return null;
            var comps = allSolutions.SelectMany( s => s.CKSetupComponentProjects )
                                    .SelectMany( p => p.TargetFrameworks.AtomicTraits
                                           .Select( t => new CKSetupComponent( p.PrimarySolutionRelativeFolderPath, t ) ) );
            if( !comps.Any() )
            {
                monitor.Warn( "SolutionSettings.ProduceCKSetupComponents is true but no projects are in CKSetupComponentProjects." );
            }
            StringBuilder b = new StringBuilder();
            b.AppendLine( "return new CKSetupComponent[]{" );
            bool atLeastOne = false;
            foreach( var c in comps )
            {
                b.AppendLine( atLeastOne ? "," : "" );
                atLeastOne = true;
                b.Append( "new CKSetupComponent( \"" ).Append( c.ProjectPath ).Append( "\", \"" ).Append( c.TargetFramework ).Append( "\" )" );
            }
            b.AppendLine().Append( "};" );
            text = text.Replace( m.Value, b.ToString() );
            return text;

        }

        string AdaptStandardCheckRepositoryForPushFeeds( string text )
        {
            Match m = Regex.Match( text, @"return new NuGetHelper\.Feed\[\]{.*?};", RegexOptions.Singleline|RegexOptions.CultureInvariant );
            if( !m.Success )
            {
                throw new Exception( "Expected pattern return new NuGetHelper.Feed[]{...} in Build.StandardCheckRepository.cs." );
            }
            StringBuilder b = new StringBuilder( );
            b.AppendLine( "return new NuGetHelper.Feed[]{" );
            bool atLeastOne = false;
            foreach( var info in _settings.ArtifactTargets.Select( a => a.Info ).OfType<INuGetFeedInfo>() )
            {
                b.AppendLine( atLeastOne ? "," : "" );
                atLeastOne = true;
                switch( info )
                {
                    case NuGetAzureFeedInfo a:
                        b.Append( "new SignatureVSTSFeed( \"" ).Append( a.Organization ).Append( "\", \"" ).Append( a.FeedName ).Append( "\" )" );
                        break;
                    case NuGetStandardFeedInfo n:
                        b.Append( "new RemoteFeed( \"" ).Append( n.Name).Append( "\", \"" )
                                                        .Append( n.Url ).Append( "\", \"" )
                                                        .Append( n.SecretKeyName ).Append( "\" )" );
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
            return r.Replace( text, "$1"+name+"$2" );
        }
    }
}
