using CK.Core;
using CK.Env.DependencyModel;

using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace CK.Env.Plugin
{
    public class CodeCakeBuilderFolder : PluginFolderBase
    {
        readonly SolutionDriver _driver;

        public CodeCakeBuilderFolder( GitRepository f, SolutionDriver driver, NormalizedPath branchPath )
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
            DeleteFileOrFolder( m, "RepositoryInfo.xsd" );

            DeleteFileOrFolder( m, "Build.NuGetHelper.cs" );
            DeleteFileOrFolder( m, "Build.StandardCheckRepository.cs" );
            DeleteFileOrFolder( m, "Build.StandardCreateNuGetPackages.cs" );
            DeleteFileOrFolder( m, "Build.StandardPushNuGetPackages.cs" );
            DeleteFileOrFolder( m, "Build.StandardSolutionBuild.cs" );
            DeleteFileOrFolder( m, "Build.StandardUnitTests.cs" );
            DeleteFileOrFolder( m, "Build.StandardCheckRepositoryWithoutNuGet.cs" );
            DeleteFileOrFolder( m, "CakeExtensions/NpmGetPackagesToPublish.cs" );
            DeleteFileOrFolder( m, "Build.SetCIVersionOnRunner.cs" );
            DeleteFileOrFolder( m, "npm/Build.NpmHelper.cs" );
            DeleteFileOrFolder( m, "Abstractions/ArtifactRepository.cs" );

            DeleteFileOrFolder( m, "dotnet/Build.NugetRepository.cs" );
            DeleteFileOrFolder( m, "dotnet/Build.StandardUnitTests.cs" );
            DeleteFileOrFolder( m, "dotnet/Build.StandardCheckRepository.cs" );
            DeleteFileOrFolder( m, "dotnet/Build.StandardSolutionBuild.cs" );
            DeleteFileOrFolder( m, "dotnet/Build.StandardCreateNuGetPackages.cs" );
            DeleteFileOrFolder( m, "dotnet/Build.StandardPushNuGetPackages.cs" );

            DeleteFileOrFolder( m, "CakeExtensions/NpmGet.cs" );
            DeleteFileOrFolder( m, "npm/Build.NpmRepository.cs" );
            DeleteFileOrFolder( m, "npm/Build.StandardNpmBuild.cs" );
            DeleteFileOrFolder( m, "npm/Build.StandardNpmUnitTests.cs" );
            DeleteFileOrFolder( m, "Abstractions/ISolutionProducingArtifact.cs" );
            DeleteFileOrFolder( m, "Abstractions/ISolution.cs" );


            // Root files
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

                DeleteFileOrFolder( m, "dotnet/DotnetSolution.cs" );
                DeleteFileOrFolder( m, "dotnet/Build.StandardSolutionBuild.cs" );
                DeleteFileOrFolder( m, "dotnet/Build.NuGetArtifactType.cs" );
                DeleteFileOrFolder( m, "dotnet/Build.NuGetHelper.cs" );
                DeleteFileOrFolder( m, "dotnet/Build.StandardCreateNuGetPackages.cs" );
                DeleteFileOrFolder( m, "dotnet/Build.StandardUnitTests.cs" );
            }

        }

        string AdaptBuild( IActivityMonitor m, ISolution s, string text )
        {
            string jsSupport = "using Cake.Npm;" + Environment.NewLine + "using Cake.Npm.RunScript;" + Environment.NewLine;

            text = text.Replace( jsSupport, String.Empty );
            if( s.Projects.Any( p => p.Type == "js" ) )
            {
                text = jsSupport + text;
            }
            var mOld = Regex.Match( text, @"SimpleRepositoryInfo\s+gitInfo\s+=\s+Cake\.GetSimpleRepositoryInfo\(\);\s*", RegexOptions.Singleline );
            if( mOld.Success )
            {
                m.Info( $"Auto upgrading Build.cs file." );
                text = text.Remove( mOld.Index, mOld.Length ).Replace( "gitInfo.IsValid", "globalInfo.IsValid" );
                text = Regex.Replace( text, @"CreateStandardGlobalInfo\(\s*gitInfo\s*\)", "CreateStandardGlobalInfo()" );
            }
            // v10.0.0
            text = text.Replace( "globalInfo.PushArtifacts();", "/* Please add async on the Does: .Does( async() => ...) above.*/await globalInfo.PushArtifactsAsync();" );
            text = text.Replace( "CleanDirectories( globalInfo.ReleasesFolder )", "CleanDirectories( globalInfo.ReleasesFolder.ToString() )" );

            mOld = Regex.Match( text, @"\[AddPath\(.*$", RegexOptions.Multiline );
            if( mOld.Success )
            {
                text = text.Remove( mOld.Index, mOld.Length );
            }
            mOld = Regex.Match( text, @".*$", RegexOptions.Multiline );
            if( mOld.Success )
            {
                text = text.Remove( mOld.Index, mOld.Length );
            }
            return text;
        }
    }
}
