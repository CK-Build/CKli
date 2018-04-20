using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli
{
    public class XGlobalCIBuildAction : XAction
    {
        readonly XSolutionCentral _solutions;
        readonly FileSystem _fileSystem;
        readonly XPublishedPackageFeeds _localPackages;

        public XGlobalCIBuildAction(
            Initializer intializer,
            FileSystem fileSystem,
            ActionCollector collector,
            XPublishedPackageFeeds localPackages,
            XSolutionCentral solutions )
            : base( intializer, collector )
        {
            _fileSystem = fileSystem;
            _solutions = solutions;
            _localPackages = localPackages;
        }

        public override bool Run( IActivityMonitor m )
        {
            // Consider all GitFolders that contains at least a solution definition in 'develop' branch.
            var gitFolders = _solutions.AllDevelopSolutions.Select( s => s.GitBranch.Parent.GitFolder )
                                .Distinct()
                                .ToList();

            foreach( var g in gitFolders )
            {
                if( !g.CheckoutAndPull( m, "develop" ) ) return false;
            }

            var environmentVariables = new List<(string, string)>();
            Console.Write( "Enter MYGET_CI_API_KEY to push packages to remote feed: " );
            string apiKey = Console.ReadLine();
            if( !String.IsNullOrEmpty( apiKey ) )
            {
                environmentVariables.Add( ("MYGET_CI_API_KEY", apiKey) );
            }

            Console.Write( "Enter https://cksetup.invenietis.net/ key to push components: " );
            string storePushKey = Console.ReadLine();
            if( !String.IsNullOrEmpty( storePushKey ) )
            {
                environmentVariables.Add( ("CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", storePushKey+"|https://cksetup.invenietis.net") );
            }

            var all = _solutions.AllDevelopSolutions.ToDictionary( s => s.Solution, s => s.GitBranch.Parent );
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

                    foreach( var build in list.Skip( startAt ) )
                    {
                        using( m.OpenInfo( $"{build.Index} - {build.Solution}" ) )
                        {
                            //Console.WriteLine( "Hit 'c' to continue. Other key to stop." );
                            //if( Console.ReadKey().KeyChar != 'c' ) return true;

                            // Updates all projects (even BuildProjects) with locally available
                            // better version.
                            var toUp = deps.ProjectDependencies.DependencyTable
                                                .Where( d => d.SourceProject.Project.PrimarySolution == build.Solution
                                                             && !d.IsExternalDependency )
                                                .Select( d => (
                                                        Row: d,
                                                        Version: _localPackages.GetLocalLastVersion( m, d.PackageId, false )) )
                                                .Where( d => d.Version == null || d.Version != d.Row.RawPackageDependency.Version )
                                                .ToList();
                            var missings = toUp.Where( d => d.Version == null );
                            if( missings.Any() )
                            {
                                m.Fatal( $"Packages not found locally: {missings.Select( u => u.Row.PackageId ).Concatenate()}." );
                                return false;
                            }
                            var downgrade = toUp.Where( d => d.Version < d.Row.RawPackageDependency.Version );
                            if( downgrade.Any() )
                            {
                                foreach( var d in downgrade )
                                {
                                    m.Fatal( $"Local package {d.Row.PackageId} found locally in version {d.Version} but current reference has a greater version {d.Row.RawPackageDependency.Version}." );
                                }
                                return false;
                            }
                            using( m.OpenInfo( $"Upgrading {toUp.GroupBy( u => u.Row.PackageId).Count()} locally available packages." ) )
                            {
                                foreach( var u in toUp )
                                {
                                    u.Row.SourceProject.Project.SetPackageReferenceVersion( m, u.Row.SourceProject.Project.TargetFrameworks, u.Row.PackageId, u.Version );
                                }
                                if( !build.Solution.Save( m, _fileSystem ) ) return false;
                            }

                            var gitFolder = all[build.Solution].GitFolder;
                            if( !gitFolder.Commit( m, "Global Build from CK-Env." ).Success ) return false;

                            bool resetSuccess = false;
                            using( Util.CreateDisposableAction( () => resetSuccess = gitFolder.ResetHard( m ) ) )
                            {
                                if( !gitFolder.EnsureLocalFeedNuGetSource( m ) ) return false;
                                if( !gitFolder.SetRepositoryXmlIgnoreDirtyFolders( m ) ) return false;
                                var path = _fileSystem.GetFileInfo( build.Solution.SolutionFolderPath ).PhysicalPath;
                                if( !Run( m, path, "dotnet", "run --project CodeCakeBuilder -autointeraction", environmentVariables ) ) return false;
                                if( !CopyReleasesPackagesToLocalFolder( m, _localPackages, build.Solution, false ) ) return false;
                            }
                            if( !resetSuccess ) return false;
                        }
                    }
                }
            }
            return true;
        }

        static internal bool CopyReleasesPackagesToLocalFolder( IActivityMonitor m, XPublishedPackageFeeds localPackages, Solution solution, bool toBlankFolder )
        {
            using( m.OpenInfo( $"Copying released Packages to LocalFeed{(toBlankFolder ? "/Blanck" : "")}." ) )
            {
                try
                {
                    var targetFolder = toBlankFolder
                                        ? localPackages.EnsureLocalFeedBlankFolder( m ).PhysicalPath
                                        : localPackages.EnsureLocalFeedFolder( m ).PhysicalPath;
                    var releasesFolder = solution.SolutionFolderPath.Combine( "CodeCakeBuilder/Releases" );
                    var packages = Directory.GetFiles( localPackages.FileSystem.GetFileInfo( releasesFolder ).PhysicalPath, "*.nupkg" );
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

        internal static bool Run( IActivityMonitor m,
                         string workingDir,
                         string fileName,
                         string arguments,
                         IEnumerable<(string, string)> environmentVariables = null )
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
                foreach( var kv in environmentVariables ) cmdStartInfo.EnvironmentVariables.Add( kv.Item1, kv.Item2 );
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
