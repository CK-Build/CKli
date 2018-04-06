using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using CKSetup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli
{
    public class XGlobalBlankBuildAction : XAction
    {
        readonly XSolutionCentral _solutions;
        readonly FileSystem _fileSystem;
        readonly XPublishedPackageFeeds _localPackages;

        public XGlobalBlankBuildAction(
            Initializer intializer,
            FileSystem fileSystem,
            XPublishedPackageFeeds localPackages,
            ActionCollector collector,
            XSolutionCentral solutions )
            : base( intializer, collector )
        {
            _fileSystem = fileSystem;
            _solutions = solutions;
            _localPackages = localPackages;
        }

        public override bool Run( IActivityMonitor m )
        {
            var gitFolders = _solutions.AllDevelopSolutions.Select( s => s.GitBranch.Parent.GitFolder ).Distinct().ToList();
            using( m.OpenTrace( $"Blank build of {_solutions.AllDevelopSolutions.Count} solutions in {gitFolders.Count} git folders." ) )
            {
                foreach( var g in gitFolders )
                {
                    if( !g.SwitchFromDevelopToBlankDev( m ) ) return false;
                }
                var blankSolutions = _solutions.AllDevelopSolutions.Select( x => (XSolution: x, Solution: x.GetSolutionInBranch( m, GitFolder.BlanckDevBranchName ) ) );
                if( blankSolutions.Any( s => s.Solution == null ) ) return false;

                var all = blankSolutions.ToDictionary( s => s.Solution, s => s.XSolution.GitBranch.Parent );
                var deps = DependencyContext.Create( m, all.Keys );
                if( deps == null ) return false;
                SolutionDependencyResult r = deps.AnalyzeDependencies( m, SolutionSortStrategy.EverythingExceptBuildProjects );
                if( r.HasError ) r.RawSorterResult.LogError( m );
                else
                {
                    using( m.OpenInfo( "Solutions to build:" ) )
                    {
                        var list = r.DependencyTable.GroupBy( d => d.Index )
                                                 .OrderBy( g => g.Key )
                                                 .Select( g => (Index: g.Key, Rows: g.ToList()) )
                                                 .Select( g => (
                                                                g.Index,
                                                                g.Rows[0].Solution,
                                                                HasAtLeastOneTargetProject: g.Rows[0].Target != null,
                                                                g.Rows) )
                                                 .ToList();
                        int startAt = 0;
                        foreach( var build in list )
                        {
                            m.Info( $"{build.Index} - {build.Solution} {(build.HasAtLeastOneTargetProject ? "(has dependencies)" : "")}" );
                        }
                        Console.Write( "Start at:" );
                        while( !int.TryParse( Console.ReadLine(), out startAt ) ) ;

                        string blankStore = EnsureBlankStore( m );
                        var envVariables = new KeyValuePair<string, string>[]
                        {
                            new KeyValuePair<string, string>("CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", blankStore)
                        };

                        foreach( var build in list.Skip( startAt ) )
                        {
                            using( m.OpenInfo( $"{build.Index} - {build.Solution}" ) )
                            {

                                if( build.HasAtLeastOneTargetProject )
                                {
                                    var toUpgrade = build.Rows.Select( d => (Row: d, Version: _localPackages.GetLocalLastVersion( m, d.Target.Name, true )) )
                                                              .ToList();
                                    var missings = toUpgrade.Where( u => u.Version == null );
                                    if( missings.Any() )
                                    {
                                        m.Fatal( $"Packages not found locally: {missings.Select( u => u.Row.Target.Name ).Concatenate()}." );
                                        return false;
                                    }
                                    using( m.OpenInfo( $"Upgrading {toUpgrade.GroupBy( u => u.Row.Target ).Count()} locally available packages." ) )
                                    {
                                        foreach( var u in toUpgrade )
                                        {
                                            u.Row.Origin.SetPackageReferenceVersion( m, u.Row.Origin.TargetFrameworks, u.Row.Target.Name, u.Version );
                                        }
                                        if( !build.Solution.Save( m, _fileSystem ) ) return false;
                                    }
                                }

                                var gitFolder = all[build.Solution].GitFolder;
                                var commitResult = gitFolder.Commit( m, "Blank Build from CK-Env." );
                                if( !commitResult.Success ) return false;

                                var path = _fileSystem.GetFileInfo( build.Solution.SolutionFolderPath ).PhysicalPath;
                                if( !Run( m, path, "dotnet", "run --project CodeCakeBuilder -autointeraction", envVariables ) ) return false;
                                if( !CopyReleasesPackagesToLocalFolder( m, build.Solution, true ) ) return false;
                            }
                        }
                    }
                }
            }
            return true;
        }

        string EnsureBlankStore( IActivityMonitor m )
        {
            string path = Path.Combine( _localPackages.EnsureLocalFeedBlankFolder( m ).PhysicalPath, "CKSetupStore" );
            if( !Directory.Exists( path ) ) Directory.CreateDirectory( path );
            using( var s = LocalStore.OpenOrCreate( m, path ) )
            {
                s.PrototypeStoreUrl = Facade.DefaultStoreUrl;
            }
            return path;
        }

        bool CopyReleasesPackagesToLocalFolder( IActivityMonitor m, Solution solution, bool toBlankFolder )
        {
            using( m.OpenInfo( $"Copying released Packages to LocalFeed{(toBlankFolder ? "/Blanck" : "")}." ) )
            {
                try
                {
                    var targetFolder = toBlankFolder
                                        ? _localPackages.EnsureLocalFeedBlankFolder( m ).PhysicalPath
                                        : _localPackages.EnsureLocalFeedFolder( m ).PhysicalPath;
                    var releasesFolder = solution.SolutionFolderPath.Combine( "CodeCakeBuilder/Releases" );
                    var packages = Directory.GetFiles( _fileSystem.GetFileInfo( releasesFolder ).PhysicalPath, "*.nupkg" );
                    foreach( var p in packages )
                    {
                        var target = Path.Combine( targetFolder, Path.GetFileName( p ) );
                        File.Copy( p, target, true );
                    }
                    return true;
                }
                catch( Exception ex )
                {
                    m.Fatal( ex );
                    return false;
                }
            }
        }

        static bool Run( IActivityMonitor m,
                         string workingDir,
                         string fileName,
                         string arguments,
                         IEnumerable<KeyValuePair<string,string>> environmentVariables )
        {
            ProcessStartInfo cmdStartInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
            if( environmentVariables != null )
            {
                foreach( var kv in environmentVariables ) cmdStartInfo.EnvironmentVariables.Add( kv.Key, kv.Value );
            }
            cmdStartInfo.Arguments = arguments;
            using( m.OpenTrace( $"{fileName} {cmdStartInfo.Arguments}" ) )
            using( Process cmdProcess = new Process() )
            {
                cmdProcess.StartInfo = cmdStartInfo;
                cmdProcess.ErrorDataReceived += ( o, e ) => { if( !string.IsNullOrEmpty( e.Data ) ) m.Info( "<StdErr> " + e.Data ); };
                cmdProcess.OutputDataReceived += ( o, e ) => { if( e.Data != null ) m.Info( "<StdOut> " + e.Data ); };
                cmdProcess.Start();
                cmdProcess.BeginErrorReadLine();
                cmdProcess.BeginOutputReadLine();
                cmdProcess.WaitForExit();

                if( cmdProcess.ExitCode != 0 )
                {
                    m.Error( $"Process returned ExitCode {cmdProcess.ExitCode}." );
                    return false;
                }
                return true;
            }
        }
    }


}
