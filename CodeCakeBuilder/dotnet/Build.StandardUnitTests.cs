using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Solution;
using Cake.Common.Tools.DotNetCore;
using Cake.Common.Tools.DotNetCore.Test;
using Cake.Common.Tools.NUnit;
using Cake.Core.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CodeCake
{
    public partial class Build
    {

        void StandardUnitTests( StandardGlobalInfo globalInfo, IEnumerable<SolutionProject> testProjects )
        {
            string memoryFilePath = $"CodeCakeBuilder/UnitTestsDone.{globalInfo.GitInfo.CommitSha}.txt";

            void WriteTestDone( FilePath test )
            {
                if( globalInfo.GitInfo.IsValid ) System.IO.File.AppendAllLines( memoryFilePath, new[] { test.ToString() } );
            }

            bool CheckTestDone( FilePath test )
            {
                bool done = System.IO.File.Exists( memoryFilePath )
                            ? System.IO.File.ReadAllLines( memoryFilePath ).Contains( test.ToString() )
                            : false;
                if( done )
                {
                    if( !globalInfo.GitInfo.IsValid )
                    {
                        Cake.Information( "Dirty commit: tests are run again (base commit tests were successful)." );
                        done = false;
                    }
                    else Cake.Information( "Test already successful on this commit." );
                }
                return done;
            }
            var testDlls = testProjects.Select
                <SolutionProject,
                   (FilePath csprojPath,
                   DirectoryPath projectPath,
                   DirectoryPath buildDirectoryNetCoreApp21,
                   FilePath netCoreAppDll21,
                   DirectoryPath buildDirectoryNetCoreApp22,
                   FilePath netCoreAppDll22,
                   DirectoryPath buildDirectoryNet461,
                   FilePath net461Dll,
                   FilePath net461Exe)>( p =>
                    {
                        var buildDirNetcore21 = p.Path.GetDirectory().Combine( "bin/" + globalInfo.BuildConfiguration + "/netcoreapp2.1/" );
                        var buildDirNetcore22 = p.Path.GetDirectory().Combine( "bin/" + globalInfo.BuildConfiguration + "/netcoreapp2.2/" );
                        var buildDirNet461 = p.Path.GetDirectory().Combine( "bin/" + globalInfo.BuildConfiguration + "/net461/" );
                        return (
                            p.Path,
                            p.Path.GetDirectory(),
                            buildDirNetcore21,
                            buildDirNetcore21.CombineWithFilePath( p.Name + ".dll" ),
                            buildDirNetcore22,
                            buildDirNetcore22.CombineWithFilePath( p.Name + ".dll" ),
                            buildDirNet461,
                            buildDirNet461.CombineWithFilePath( p.Name + ".dll" ),
                            buildDirNet461.CombineWithFilePath( "/net461/" + p.Name + ".exe" )
                        );
                   } );

            foreach(
                var (csprojPath,projectPath,
                buildDirectoryNetCoreApp21,
                netCoreAppDll21,
                buildDirectoryNetCoreApp22,
                netCoreAppDll22,
                buildDirectoryNet461,
                net461Dll,
                net461Exe) in testDlls )
            {
                var net461 = Cake.FileExists( net461Dll )
                                ? net461Dll
                                : Cake.FileExists( net461Exe )
                                    ? net461Exe
                                    : null;
                bool isVsTests =
                    Cake.FileExists( buildDirectoryNetCoreApp21.CombineWithFilePath( "NUnit3.TestAdapter.dll" ))
                    || Cake.FileExists( buildDirectoryNetCoreApp22.CombineWithFilePath( "NUnit3.TestAdapter.dll" ) )
                    || Cake.FileExists( buildDirectoryNet461.CombineWithFilePath( "NUnit3.TestAdapter.dll" ) );
                if( net461 != null && !isVsTests )
                {
                    Cake.Information( $"Testing via NUnit (net461): {net461}" );
                    if( !CheckTestDone( net461 ) )
                    {
                        Cake.NUnit( new[] { net461 }, new NUnitSettings()
                        {
                            Framework = "v4.5",
                            ResultsFile = projectPath.CombineWithFilePath( "TestResult.Net461.xml" )
                        } );
                        WriteTestDone( net461 );
                    }
                }
                if( Cake.FileExists( netCoreAppDll21 ) )
                {
                    TestNetCore( csprojPath.FullPath, netCoreAppDll21, "netcoreapp2.1" );
                }
                if( Cake.FileExists( netCoreAppDll22 ) )
                {
                    TestNetCore( csprojPath.FullPath, netCoreAppDll22, "netcoreapp2.2" );
                }
            }

            void TestNetCore( string projectPath, Cake.Core.IO.FilePath dllFilePath, string framework )
            {
                var e = XDocument.Load( projectPath ).Root;
                if( e.Descendants( "PackageReference" ).Any( r => r.Attribute( "Include" )?.Value == "Microsoft.NET.Test.Sdk" ) )
                {
                    Cake.Information( $"Testing via VSTest ({framework}): {dllFilePath}" );
                    if( CheckTestDone( dllFilePath ) ) return;
                    Cake.DotNetCoreTest( projectPath, new DotNetCoreTestSettings()
                    {
                        Configuration = globalInfo.BuildConfiguration,
                        Framework = framework,
                        NoRestore = true,
                        NoBuild = true,
                        Logger = "trx"
                    } );
                }
                else
                {
                    Cake.Information( $"Testing via NUnitLite ({framework}): {dllFilePath}" );
                    if( CheckTestDone( dllFilePath ) ) return;
                    Cake.DotNetCoreExecute( dllFilePath );
                }
                WriteTestDone( dllFilePath );
            }
        }

    }
}
