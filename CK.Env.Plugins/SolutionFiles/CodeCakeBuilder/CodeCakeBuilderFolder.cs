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
        readonly ICommonSolutionSpec _settings;

        public CodeCakeBuilderFolder( GitFolder f, SolutionDriver driver, ICommonSolutionSpec settings, NormalizedPath branchPath )
            : base( f, branchPath, "CodecakeBuilder" )
        {
            _driver = driver;
            _settings = settings;
        }

        protected override void DoApplySettings( IActivityMonitor m )
        {
            var s = _driver.GetPrimarySolution( m );
            if( s == null ) return;

            bool hasDotNetPackages = s.PublishedProjects.Any();
            bool needDotNetBuild = hasDotNetPackages || s.AllProjects.Except( s.BuildProjects ).Any();

            // Clean old CodeCakeBuilders files
            DeleteFile( m, "Build.NuGetHelper.cs" );
            DeleteFile( m, "Build.StandardCheckRepository.cs" );
            DeleteFile( m, "Build.StandardCreateNuGetPackages.cs" );
            DeleteFile( m, "Build.StandardPushNuGetPackages.cs" );
            DeleteFile( m, "Build.StandardSolutionBuild.cs" );
            DeleteFile( m, "Build.StandardUnitTests.cs" );
            DeleteFile( m, "Build.StandardCheckRepositoryWithoutNuGet.cs" );
            DeleteFile( m, "CakeExtensions/NpmGetPackagesToPublish.cs" );
            DeleteFile( m, "Build.SetCIVersionOnRunner.cs" );
            DeleteFile( m, "npm/Build.NpmHelper.cs" );
            DeleteFile( m, "Abstractions/ArtifactRepository.cs" );
            DeleteFile( m, "dotnet/Build.NugetRepository.cs" );
            DeleteFile( m, "dotnet/Build.StandardCheckRepository.cs" );
            DeleteFile( m, "dotnet/Build.StandardPushNuGetPackages.cs" );
            DeleteFile( m, "CakeExtensions/NpmGet.cs" );
            DeleteFile( m, "npm/Build.NpmRepository.cs" );
            DeleteFile( m, "npm/Build.StandardNpmBuild.cs" );
            DeleteFile( m, "npm/Build.StandardNpmUnitTests.cs" );


            // Root files
            SetTextResource( m, "InstallCredentialProvider.ps1" );
            SetTextResource( m, "Program.cs" );
            SetTextResource( m, "StandardGlobalInfo.cs" );
            SetTextResource( m, "Build.CreateStandardGlobalInfo.cs" );

            UpdateTextResource( m, "Build.cs", AdaptBuild );

            // Abstractions files
            SetTextResource( m, "Abstractions/Artifact.cs" );
            SetTextResource( m, "Abstractions/ArtifactFeed.cs" );
            SetTextResource( m, "Abstractions/ArtifactInstance.cs" );
            SetTextResource( m, "Abstractions/ArtifactPush.cs" );
            SetTextResource( m, "Abstractions/ArtifactType.cs" );
            SetTextResource( m, "Abstractions/ILocalArtifact.cs" );


            if( needDotNetBuild )
            {
                SetTextResource( m, "dotnet/Build.StandardSolutionBuild.cs" );
                if( hasDotNetPackages )
                {
                    SetTextResource( m, "dotnet/Build.NuGetArtifactType.cs", AdaptBuildNugetRepositoryForPushFeeds );
                    SetTextResource( m, "dotnet/Build.NuGetHelper.cs" );
                    SetTextResource( m, "dotnet/Build.StandardCreateNuGetPackages.cs" );
                }
                else
                {
                    m.Info( "Removing build files related to NuGet packaging." );
                    DeleteFile( m, "dotnet/Build.NuGetArtifactType.cs" );
                    DeleteFile( m, "dotnet/Build.NuGetHelper.cs" );
                    DeleteFile( m, "dotnet/Build.StandardCreateNuGetPackages.cs" );
                }
                if( _settings.NoDotNetUnitTests )
                {
                    m.Info( "Removing Build.StandardUnitTests since NoDotNetUnitTests is true." );
                    DeleteFile( m, "dotnet/Build.StandardUnitTests.cs" );
                }
                else
                {
                    SetTextResource( m, "dotnet/Build.StandardUnitTests.cs" );
                }
            }
            else
            {
                m.Info( "Removing all files for .Net projects." );

                DeleteFile( m, "dotnet/Build.StandardSolutionBuild.cs" );
                DeleteFile( m, "dotnet/Build.NuGetArtifactType.cs" );
                DeleteFile( m, "dotnet/Build.NuGetHelper.cs" );
                DeleteFile( m, "dotnet/Build.StandardCreateNuGetPackages.cs" );
                DeleteFile( m, "dotnet/Build.StandardUnitTests.cs" );
            }

            bool useNpm = _driver.GetAllNPMProjects( m ).Any();
            if( useNpm )
            {
                //CakeExtensions
                SetTextResource( m, "CakeExtensions/NpmDistTagRunner.cs" );
                SetTextResource( m, "CakeExtensions/NpmView.cs" );
                //npm itself
                SetTextResource( m, "npm/Build.NPMArtifactType.cs", AdaptBuildNPMArtifactForPushFeeds );
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
                        b.Append( "new SignatureVSTSFeed( this, \"" )
                                    .Append( a.Organization ).Append( "\", \"" )
                                    .Append( a.FeedName ).Append( "\" )" );
                        break;
                    case NuGetStandardFeedInfo n:
                        b.Append( "new RemoteFeed( this, \"" )
                                            .Append( n.Name ).Append( "\", \"" )
                                            .Append( n.Url ).Append( "\", \"" )
                                            .Append( n.SecretKeyName ).Append( "\" )" );
                        break;
                }
            }
            b.AppendLine().Append( "};" );
            text = text.Replace( m.Value, b.ToString() );
            return text;
        }

        string AdaptBuildNPMArtifactForPushFeeds( string text )
        {
            Match m = Regex.Match( text, @"return new NPMRemoteFeedBase\[\]{.*?};", RegexOptions.Singleline | RegexOptions.CultureInvariant );
            if( !m.Success )
            {
                throw new Exception( "Expected pattern return new NPMRemoteFeedBase[]{...} in Build.NPMArtifactType.cs." );
            }
            StringBuilder b = new StringBuilder();
            b.AppendLine( "return new NPMRemoteFeedBase[]{" );
            bool atLeastOne = false;
            foreach( var info in _settings.ArtifactTargets.Select( a => a.Info ).OfType<INPMFeedInfo>() )
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
