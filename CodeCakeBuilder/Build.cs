using Cake.Common.IO;
using Cake.Common.Solution;
using Cake.Core;
using Cake.Npm;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using SimpleGitVersion;
using System.Linq;
using Cake.Npm.Install;
using Cake.Npm.RunScript;

namespace CodeCake
{
    [AddPath( "%UserProfile%/.nuget/packages/**/tools*" )]
    public partial class Build : CodeCakeHost
    {
        public Build()
        {
            Cake.Log.Verbosity = Verbosity.Diagnostic;
            string solutionFilePath = Cake.GetFiles( "*.sln" ).Single().FullPath;
            var releasesDir = Cake.Directory( "CodeCakeBuilder/Releases" );
            var projects = Cake.ParseSolution( solutionFilePath )
                                       .Projects
                                       .Where( p => !(p is SolutionFolder)
                                                    && p.Name != "CodeCakeBuilder" );

            // We do not generate NuGet packages for /Tests projects for this solution.
            var projectsToPublish = projects
                                        .Where( p => !p.Path.Segments.Contains( "Tests" ) );
            var packageDir = Cake.Directory( "js" );
            var packageJsonPath = packageDir.Path.CombineWithFilePath( "package.json" ).FullPath;
            var packageLockJsonPath = packageDir.Path.CombineWithFilePath( "package-lock.json" ).FullPath;
            SimpleRepositoryInfo gitInfo = Cake.GetSimpleRepositoryInfo();
            CheckRepositoryInfo globalInfo = new CheckRepositoryInfo( Cake, gitInfo );
            NuGetRepositoryInfo nugetInfo = null;
            // Configuration is either "Debug" or "Release".
            Task( "Check-Repository" )
                .Does( () =>
                {
                    globalInfo = StandardCheckRepositoryWithoutNuget( gitInfo );
                    globalInfo.AddAndInitRepository( nugetInfo = new NuGetRepositoryInfo( Cake, globalInfo, projectsToPublish ) );
                    if( globalInfo.ShouldStop )
                    {
                        Cake.TerminateWithSuccess( "All packages from this commit are already available. Build skipped." );
                    }
                } );
            Task( "Clean" )
                .IsDependentOn( "Check-Repository" )
                .Does( () =>
                {
                    Cake.CleanDirectories( projects.Select( p => p.Path.GetDirectory().Combine( "bin" ) ) );
                    Cake.CleanDirectories( releasesDir );
                    Cake.DeleteFiles( "Tests/**/TestResult*.xml" );
                } );

            Task( "Build" )
                .IsDependentOn( "Check-Repository" )
                .IsDependentOn( "Clean" )
                .Does( () =>
                {
                    StandardSolutionBuild( solutionFilePath, nugetInfo );
                } );

            Task( "Unit-Testing" )
                .IsDependentOn( "Build" )
                .WithCriteria( () => Cake.InteractiveMode() == InteractiveMode.NoInteraction
                                     || Cake.ReadInteractiveOption( "RunUnitTests", "Run Unit Tests?", 'Y', 'N' ) == 'Y' )
                .Does( () =>
                {
                    var testProjects = projects.Where( p => p.Name.EndsWith( ".Tests" ) );
                    StandardUnitTests( nugetInfo, testProjects );
                } );


            Task( "Create-NuGet-Packages" )
                .WithCriteria( () => gitInfo.IsValid )
                .IsDependentOn( "Unit-Testing" )
                .Does( () =>
                {
                    StandardCreateNuGetPackages( nugetInfo, releasesDir );
                } );

            Task( "Push-NuGet-Packages" )
                .IsDependentOn( "Create-NuGet-Packages" )
                .WithCriteria( () => gitInfo.IsValid )
                .Does( () =>
                {
                    globalInfo.PushArtifacts( releasesDir );
                } );
            // The Default task for this script can be set here.
            Task( "Default" )
                .IsDependentOn( "Push-NuGet-Packages" );
        }

    }
}
