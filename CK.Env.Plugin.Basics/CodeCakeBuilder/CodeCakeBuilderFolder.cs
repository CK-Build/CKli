using CK.Core;
using CK.Env.DependencyModel;
using CK.Text;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.Env.Plugin
{
    public class CodeCakeBuilderFolder : PluginFolderBase
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _solutionSpec;

        public CodeCakeBuilderFolder( GitFolder f, SolutionDriver driver, SolutionSpec settings, NormalizedPath branchPath )
            : base( f, branchPath, "CodeCakeBuilder", "Basics/Res" )
        {
            _driver = driver;
            _solutionSpec = settings;
        }

        protected override void DoApplySettings( IActivityMonitor m )
        {
            var s = _driver.GetSolution( m );
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


            // Root files
            SetTextResource( m, "InstallCredentialProvider.ps1" );
            SetTextResource( m, "Program.cs" );
            SetTextResource( m, "StandardGlobalInfo.cs" );
            SetTextResource( m, "Build.CreateStandardGlobalInfo.cs" );

            UpdateTextResource( m, "Build.cs" );

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
                if( _solutionSpec.NoDotNetUnitTests )
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

        }
    }
}
