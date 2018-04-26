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
            var ctx = _solutions.GetGlobalReleaseContext( m, false );
            if( ctx == null ) return false;

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

            var deps = DependencyContext.Create( m, ctx.AllSolutions );
            if( deps == null ) return false;
            SolutionDependencyResult r = deps.AnalyzeDependencies( m, SolutionSortStrategy.EverythingExceptBuildProjects );
            if( r.HasError ) r.RawSorterResult.LogError( m );
            else
            {
                DisplaySolutionList( m, r );
                int startAt = 0;
                Console.Write( "Start at:" );
                while( !int.TryParse( Console.ReadLine(), out startAt ) ) ;

                foreach( var build in r.Solutions.Skip( startAt ) )
                {
                    XGlobalCIBuildAction.DisplaySolutionList( m, r, build.Index );
                    using( m.OpenInfo( $"{build.Index} - {build.Solution}" ) )
                    {
                        //Console.WriteLine( "Hit 'c' to continue. Other key to stop." );
                        //if( Console.ReadKey().KeyChar != 'c' ) return true;

                        if( !UpgradeSolutionPackagesToTheMax( m, _localPackages, deps, build.Solution, false, false ) ) return false;

                        var gitFolder = build.Solution.GitFolder;
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
            return true;
        }

        internal static void DisplaySolutionList( IActivityMonitor m, SolutionDependencyResult r, int currentIdx = -1 )
        {
            using( m.OpenInfo( "Solutions to build:" ) )
            {
                int rank = -1;
                foreach( var s in r.Solutions )
                {
                    if( rank != s.Rank )
                    {
                        rank = s.Rank;
                        m.Info( $" -- Rank {rank}" );
                    }
                    m.Info( $"{(currentIdx == s.Index ? '*' : ' ')}   {s.Index} - {s.Solution} => {s.MinimalImpacts.Count}/{s.Impacts.Count}/{s.TransitiveImpacts.Count}" );
                }
            }
        }

        internal static bool UpgradeSolutionPackagesToTheMax(
            IActivityMonitor m,
            XPublishedPackageFeeds feeds,
            DependencyContext deps,
            Solution solution,
            bool withBlankFeed,
            bool allowDowngrade )
        {
            // Updates all projects (even BuildProjects) with locally available
            // better version.
            var toUp = deps.ProjectDependencies.DependencyTable
                                .Where( d => d.SourceProject.Project.PrimarySolution == solution
                                             && !d.IsExternalDependency )
                                .Select( d => (
                                        Row: d,
                                        Version: feeds.GetLocalLastVersion( m, d.PackageId, withBlankFeed )) )
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
                    m.Log( allowDowngrade ? LogLevel.Warn : LogLevel.Fatal, $"Local package {d.Row.PackageId} found locally in version {d.Version} but current reference has a greater version {d.Row.RawPackageDependency.Version}." );
                }
                if( !allowDowngrade ) return false;
            }
            using( m.OpenInfo( $"Upgrading {toUp.GroupBy( u => u.Row.PackageId ).Count()} locally available packages." ) )
            {
                foreach( var u in toUp )
                {
                    u.Row.SourceProject.Project.SetPackageReferenceVersion( m, u.Row.SourceProject.Project.TargetFrameworks, u.Row.PackageId, u.Version );
                }
                if( !solution.Save( m, feeds.FileSystem ) ) return false;
            }
            return solution.GitFolder.Commit( m, "Global Build from CK-Env." ).Success;
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
            var encoding = Encoding.GetEncoding( 437 );
            ProcessStartInfo cmdStartInfo = new ProcessStartInfo
            {
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
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
                StringBuilder errorCapture = new StringBuilder();
                cmdProcess.StartInfo = cmdStartInfo;
                cmdProcess.ErrorDataReceived += ( o, e ) => { if( !string.IsNullOrEmpty( e.Data ) ) errorCapture.AppendLine( e.Data ); };
                cmdProcess.OutputDataReceived += ( o, e ) => { if( e.Data != null ) m.Info( "<StdOut> " + e.Data ); };
                cmdProcess.Start();
                cmdProcess.BeginErrorReadLine();
                cmdProcess.BeginOutputReadLine();
                cmdProcess.WaitForExit();
                if( errorCapture.Length > 0 )
                {
                    m.Error( $"Received errors on <StdErr>:" );
                    m.Error( errorCapture.ToString() );
                }
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
