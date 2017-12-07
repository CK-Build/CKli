using Cake.Common.Build;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Solution;
using Cake.Common.Tools.DotNetCore;
using Cake.Common.Tools.DotNetCore.Build;
using Cake.Common.Tools.DotNetCore.Pack;
using Cake.Common.Tools.DotNetCore.Restore;
using Cake.Common.Tools.NuGet;
using Cake.Common.Tools.NuGet.Push;
using Cake.Common.Tools.NUnit;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CodeCake
{
    /// <summary>
    /// Standard build "script".
    /// </summary>
    [AddPath( "CodeCakeBuilder/Tools" )]
    [AddPath( "packages/**/tools*" )]
    public class Build : CodeCakeHost
    {
        public Build()
        {
            Cake.Log.Verbosity = Verbosity.Diagnostic;

            const string solutionName = "CK-Env";
            const string solutionFileName = solutionName + ".sln";
            const string coreBuildProj = "CodeCakeBuilder/CoreBuild.proj";

            var projects = Cake.ParseSolution( solutionFileName )
                                       .Projects
                                       .Where( p => !(p is SolutionFolder)
                                                    && p.Name != "CodeCakeBuilder" );

            string configuration = "Debug";

            Task( "Build" )
                .Does( () =>
                {
                    Cake.DotNetCoreBuild( coreBuildProj,
                        new DotNetCoreBuildSettings(){ Configuration = configuration } );
                } );

            Task( "Unit-Testing" )
                .IsDependentOn( "Build" )
                .Does( () =>
                {
                    var testDlls = projects.Where( p => p.Name.EndsWith( ".Tests" ) ).Select( p =>
                                 new
                                 {
                                     ProjectPath = p.Path.GetDirectory(),
                                     NetCoreAppDll = p.Path.GetDirectory().CombineWithFilePath( "bin/" + configuration + "/netcoreapp2.0/" + p.Name + ".dll" ),
                                     Net461Exe = p.Path.GetDirectory().CombineWithFilePath( "bin/" + configuration + "/net461/" + p.Name + ".exe" ),
                                 } );

                    foreach( var test in testDlls )
                    {
                        Cake.Information( "Testing: {0}", test.Net461Exe );
                        Cake.NUnit( test.Net461Exe.FullPath, new NUnitSettings()
                        {
                            Framework = "v4.5",
                            ResultsFile = test.ProjectPath.CombineWithFilePath( "TestResult.Net461.xml" )
                        } );
                        Cake.Information( "Testing: {0}", test.NetCoreAppDll );
                        Cake.DotNetCoreExecute( test.NetCoreAppDll );
                    }
                } );

            // The Default task for this script can be set here.
            Task( "Default" )
                .IsDependentOn( "Unit-Testing" );

        }
    }
}
