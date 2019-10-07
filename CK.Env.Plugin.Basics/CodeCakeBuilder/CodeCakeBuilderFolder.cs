using CK.Core;
using CK.Env.DependencyModel;
using CK.Text;
using System.Linq;

namespace CK.Env.Plugin
{
    public class CodeCakeBuilderFolder : PluginFolderBase
    {
        readonly SolutionDriver _driver;

        public CodeCakeBuilderFolder( GitFolder f, SolutionDriver driver, NormalizedPath branchPath )
            : base( f, branchPath, "CodeCakeBuilder", "Basics/Res" )
        {
            _driver = driver;
        }

        protected override void DoApplySettings( IActivityMonitor m )
        {
            var s = _driver.GetSolution( m, allowInvalidSolution: true );
            if( s == null ) return;

            bool needDotNetBuild = s.Projects.Any( p => p.Type == ".Net" && p != s.BuildProject );

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
            DeleteFile( m, "Abstractions/ISolutionProducingArtifact.cs" );
            DeleteFile( m, "Abstractions/ISolution.cs" );


            // Root files
            SetTextResource( m, "InstallCredentialProvider.ps1" );
            SetTextResource( m, "Program.cs" );
            SetTextResource( m, "StandardGlobalInfo.cs" );
            SetTextResource( m, "Build.CreateStandardGlobalInfo.cs" );

            UpdateTextResource( m, "Build.cs", t => AdaptBuild( m, s, t ) );

            // Abstractions files
            SetTextResource( m, "Abstractions/Artifact.cs" );
            SetTextResource( m, "Abstractions/ArtifactFeed.cs" );
            SetTextResource( m, "Abstractions/ArtifactInstance.cs" );
            SetTextResource( m, "Abstractions/ArtifactPush.cs" );
            SetTextResource( m, "Abstractions/ArtifactType.cs" );
            SetTextResource( m, "Abstractions/ILocalArtifact.cs" );
            SetTextResource( m, "Abstractions/ICIPublishWorkflow.cs" );
            SetTextResource( m, "Abstractions/ICIWorkflow.cs" );

            if( needDotNetBuild )
            {
                SetTextResource( m, "dotnet/DotnetSolution.cs" );
            }
            else
            {
                m.Info( "Removing all files for .Net projects." );

                DeleteFile( m, "dotnet/DotnetSolution.cs" );
                DeleteFile( m, "dotnet/Build.StandardSolutionBuild.cs" );
                DeleteFile( m, "dotnet/Build.NuGetArtifactType.cs" );
                DeleteFile( m, "dotnet/Build.NuGetHelper.cs" );
                DeleteFile( m, "dotnet/Build.StandardCreateNuGetPackages.cs" );
                DeleteFile( m, "dotnet/Build.StandardUnitTests.cs" );
            }

        }

        string AdaptBuild( IActivityMonitor m, ISolution s, string text )
        {
            if( !s.Projects.Any( p => p.Type == "js" ) )
            {
                text = text.Replace( "using Cake.Npm;", "" );
                text = text.Replace( "using Cake.Npm.Install;", "" );
                text = text.Replace( "using Cake.Npm.RunScript;", "" );
            }
            return text;
        }
    }
}
