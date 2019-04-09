using CK.Core;
using CK.NPMClient;
using CK.NuGetClient;
using CK.Text;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
            // Clean old CodeCakeBuilders files
            DeleteFile( m, "Build.NuGetHelper.cs" );
            DeleteFile( m, "Build.StandardCheckRepository.cs" );
            DeleteFile( m, "Build.StandardCreateNuGetPackages.cs" );
            DeleteFile( m, "Build.StandardPushNuGetPackages.cs" );
            DeleteFile( m, "Build.StandardSolutionBuild.cs" );
            DeleteFile( m, "Build.StandardUnitTests.cs" );

            // Root files
            SetTextResource( m, "InstallCredentialProvider.ps1" );
            SetTextResource( m, "Program.cs" );
            SetTextResource( m, "Build.SetCIVersionOnRunner.cs" );
            SetTextResource( m, "Build.StandardCheckRepositoryWithoutNuGet.cs" );
            SetTextResource( m, "Build.StandardPushCKSetupComponents.cs" );
            UpdateTextResource( m, "Build.cs", AdaptBuild );

            // Abstractions files
            SetTextResource( m, "Abstractions/Artifact.cs" );
            SetTextResource( m, "Abstractions/ArtifactFeed.cs" );
            SetTextResource( m, "Abstractions/ArtifactInstance.cs" );
            SetTextResource( m, "Abstractions/ArtifactRepository.cs" );


            bool useDotnet = true;
            if( useDotnet )
            {
                SetTextResource( m, "dotnet/Build.NuGetHelper.cs" );
                SetTextResource( m, "dotnet/Build.NugetRepository.cs", AdaptBuildNugetRepositoryForPushFeeds );
                SetTextResource( m, "dotnet/Build.StandardCheckRepository.cs" );
                SetTextResource( m, "dotnet/Build.StandardSolutionBuild.cs" );
                if( _settings.NoUnitTests )
                {
                    m.Info( "Removing Build.StandardUnitTests since NoUnitTests is true." );
                    DeleteFile( m, "dotnet/Build.StandardUnitTests.cs" );
                }
                else
                {
                    SetTextResource( m, "dotnet/Build.StandardUnitTests.cs" );
                }
                if( _driver.GetAllSolutions( m ).SelectMany( s => s.PublishedProjects ).Any() )
                {
                    SetTextResource( m, "dotnet/Build.StandardCreateNuGetPackages.cs" );
                    SetTextResource( m, "dotnet/Build.StandardPushNuGetPackages.cs" );
                }
                else
                {
                    m.Info( "Removing build files related to NuGet packaging." );
                    DeleteFile( m, "dotnet/Build.StandardCreateNuGetPackages.cs" );
                    DeleteFile( m, "dotnet/Build.StandardPushNuGetPackages.cs" );
                }
            }

            bool useNpm = _driver.GetAllNPMProjects( m ).Any();
            if( useNpm )
            {
                //CakeExtensions
                SetTextResource( m, "CakeExtensions/NpmDistTagRunner.cs" );
                SetTextResource( m, "CakeExtensions/NpmGet.cs" );
                SetTextResource( m, "CakeExtensions/NpmGetPackagesToPublish.cs" );
                SetTextResource( m, "CakeExtensions/NpmView.cs" );
                //npm itself
                SetTextResource( m, "npm/Build.NpmFeed.cs" );
                SetTextResource( m, "npm/Build.NpmHelper.cs" );
                SetTextResource( m, "npm/Build.NpmRepository.cs", AdaptBuildNPMRepositoryForPushFeeds );
                SetTextResource( m, "npm/Build.StandardNpmBuild.cs" );
                SetTextResource( m, "npm/Build.StandardNpmUnitTests.cs" );
            }
            else
            {
                m.Info( "Removing build files related to NPM handling." );
                DeleteFile( m, "CakeExtensions/NpmDistTagRunner.cs" );
                DeleteFile( m, "CakeExtensions/NpmGet.cs" );
                DeleteFile( m, "CakeExtensions/NpmGetPackagesToPublish.cs" );
                DeleteFile( m, "CakeExtensions/NpmView.cs" );
                DeleteFile( m, "npm/Build.NpmFeed.cs" );
                DeleteFile( m, "npm/Build.NpmHelper.cs" );
                DeleteFile( m, "npm/Build.NpmRepository.cs" );
                DeleteFile( m, "npm/Build.StandardNpmBuild.cs" );
                DeleteFile( m, "npm/Build.StandardNpmUnitTests.cs" );
            }

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

        string AdaptBuildNugetRepositoryForPushFeeds( string text )
        {
            Match m = Regex.Match( text, @"return new NuGetHelper\.NuGetFeed\[\]{.*?};", RegexOptions.Singleline | RegexOptions.CultureInvariant );
            if( !m.Success )
            {
                throw new Exception( "Expected pattern return new NuGetHelper.NuGetFeed[]{...} in Build.NugetRepository.cs." );
            }
            StringBuilder b = new StringBuilder();
            b.AppendLine( "return new NuGetHelper.NuGetFeed[]{" );
            bool atLeastOne = false;
            foreach( var info in _settings.ArtifactTargets.Select( a => a.Info ).OfType<INuGetFeedInfo>() )
            {
                b.AppendLine( atLeastOne ? "," : "" );
                atLeastOne = true;
                switch( info )
                {
                    case NuGetAzureFeedInfo a:
                        b.Append( "new SignatureVSTSFeed(cake, \"" ).Append( a.Organization ).Append( "\", \"" ).Append( a.FeedName ).Append( "\" )" );
                        break;
                    case NuGetStandardFeedInfo n:
                        b.Append( "new RemoteFeed(cake, \"" ).Append( n.Name ).Append( "\", \"" )
                                                        .Append( n.Url ).Append( "\", \"" )
                                                        .Append( n.SecretKeyName ).Append( "\" )" );
                        break;
                }
            }
            b.AppendLine().Append( "};" );
            text = text.Replace( m.Value, b.ToString() );
            return text;
        }

        string AdaptBuildNPMRepositoryForPushFeeds( string text )
        {
            Match m = Regex.Match( text, @"return new NpmRemoteFeed\[\]{.*?};", RegexOptions.Singleline | RegexOptions.CultureInvariant );
            if( !m.Success )
            {
                throw new Exception( "Expected pattern return new NpmRemoteFeed[]{...} in Build.NPMRepository.cs." );
            }
            StringBuilder b = new StringBuilder();
            b.AppendLine( "return new NpmRemoteFeed[]{" );
            bool atLeastOne = false;
            foreach( var info in _settings.ArtifactTargets.Select( a => a.Info ).OfType<INPMFeedInfo>() )
            {
                b.AppendLine( atLeastOne ? "," : "" );
                atLeastOne = true;
                switch( info )
                {
                    case NPMAzureFeedInfo a:
                        b.Append( "new VSTSNpmFeed(cake, info, \"" ).Append( a.Organization ).Append( "\", \"" ).Append( a.Url ).Append( "\" )" );
                        break;
                    case NPMStandardFeedInfo n:
                        b.Append( "new NpmRemoteFeed(cake, info, \"" ).Append( n.SecretKeyName ).Append( "\", \"" )
                                                        .Append( n.Url ).Append( "\" )" );
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
