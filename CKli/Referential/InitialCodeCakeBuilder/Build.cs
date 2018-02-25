using Cake.Common.Build;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Solution;
using Cake.Common.Text;
using Cake.Common.Tools.DotNetCore;
using Cake.Common.Tools.DotNetCore.Build;
using Cake.Common.Tools.DotNetCore.Pack;
using Cake.Common.Tools.DotNetCore.Publish;
using Cake.Common.Tools.DotNetCore.Restore;
using Cake.Common.Tools.MSBuild;
using Cake.Common.Tools.NuGet;
using Cake.Common.Tools.NuGet.Pack;
using Cake.Common.Tools.NuGet.Push;
using Cake.Common.Tools.NuGet.Restore;
using Cake.Common.Tools.NUnit;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using CK.Text;
using Code.Cake;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CodeCake
{

    [AddPath("%UserProfile%/.nuget/packages/**/tools*")]
    public class Build : CodeCakeHost
    {

        public Build()
        {
            Cake.Log.Verbosity = Verbosity.Diagnostic;

            // solutionName is the name of default solution (same as the git folder name).
            const string solutionName = "$Solution Name$";
            const string solutionFileName = solutionName + ".sln";

            var releasesDir = Cake.Directory( "CodeCakeBuilder/Releases" );
            var projects = Cake.ParseSolution( solutionFileName )
                                       .Projects
                                       .Where( p => !(p is SolutionFolder)
                                                    && p.Name != "CodeCakeBuilder" );

            // We do not generate NuGet packages for /Tests projects for this solution.
            var projectsToPublish = projects
                                        .Where( p => !p.Path.Segments.Contains( "Tests" ) );

            SimpleRepositoryInfo gitInfo = Cake.GetSimpleRepositoryInfo();

            // Configuration is either "Debug" or "Release".
            string configuration = "Debug";

            Task( "Check-Repository" )
                .Does( () =>
                 {
                     configuration = StandardCheckRepository(projectsToPublish, gitInfo);
                 });

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
                     StandardSolutionBuild(solutionFileName, gitInfo, configuration);
                 });

            Task( "Unit-Testing" )
                .IsDependentOn( "Build" )
                .WithCriteria( () => !Cake.IsInteractiveMode()
                                     || Cake.ReadInteractiveOption( "Run Unit Tests?", 'Y', 'N' ) == 'Y' )
                .Does( () =>
                 {
                     var testProjects = projects.Where(p => p.Name.EndsWith(".Tests"));
                     StandardUnitTests(configuration, testProjects);
                 });


            Task("Create-NuGet-Packages")
                .WithCriteria(() => gitInfo.IsValid)
                .IsDependentOn("Unit-Testing")
                .Does(() =>
                {
                    StandardCreateNuGetPackages(releasesDir, projectsToPublish, gitInfo, configuration);
                });

            Task("Push-NuGet-Packages")
                .IsDependentOn("Create-NuGet-Packages")
                .WithCriteria(() => gitInfo.IsValid)
                .Does(() =>
                {
                    StandardPushNuGetPackages(Cake.GetFiles(releasesDir.Path + "/*.nupkg"), gitInfo);
                });


            // The Default task for this script can be set here.
            Task( "Default" )
                .IsDependentOn( "Push-NuGet-Packages" );
        }

    }
}
