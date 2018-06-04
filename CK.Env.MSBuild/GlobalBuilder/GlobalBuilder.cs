using CK.Core;
using CK.Text;
using CKSetup;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    class GlobalBuilder
    {
        readonly SolutionDependencyResult _dependencyResult;
        readonly FileSystem _fileSystem;
        readonly ILocalFeedProvider _feeds;
        readonly ITestRunMemory _testRunMemory;
        readonly GlobalBuilderInfo _buildInfo;

        public GlobalBuilder(
            SolutionDependencyResult r,
            FileSystem fileSystem,
            ILocalFeedProvider feeds,
            ITestRunMemory testRunMemory,
            GlobalBuilderInfo buildInfo )
        {
            _dependencyResult = r;
            _fileSystem = fileSystem;
            _feeds = feeds;
            _testRunMemory = testRunMemory;
            _buildInfo = buildInfo;
        }

        public bool Build( IActivityMonitor m  )
        {
            var environmentVariables = new List<(string, string)>();
            if( _buildInfo.IsRemotesRequired == null )
            {
                Console.Write( "Do you want to push packages to remotes? (Y/N):" );
                char a;
                while( (a = Console.ReadKey().KeyChar) != 'Y' && a != 'N' ) ;
                _buildInfo.IsRemotesRequired = a == 'Y';
            }
            Debug.Assert( _buildInfo.IsRemotesRequired.HasValue );
            if( _buildInfo.IsRemotesRequired.Value )
            {
                if( !_buildInfo.EnsureRemotesAvailable( m ) ) return false;
                environmentVariables.Add( ("MYGET_CI_API_KEY", _buildInfo.MyGetApiKey) );
                environmentVariables.Add( ("MYGET_PREVIEW_API_KEY", _buildInfo.MyGetApiKey) );
                environmentVariables.Add( ("MYGET_RELEASE_API_KEY", _buildInfo.MyGetApiKey) );
                environmentVariables.Add( ("CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", _buildInfo.RemoteStorePushApiKey + "|https://cksetup.invenietis.net") );
            }
            else
            {
                string EnsureStore( IFileInfo folder, Uri prototypeUrl )
                {
                    string path = Path.Combine( folder.PhysicalPath, "CKSetupStore" );
                    if( !Directory.Exists( path ) ) Directory.CreateDirectory( path );
                    using( var s = LocalStore.OpenOrCreate( m, path ) )
                    {
                        s.PrototypeStoreUrl = prototypeUrl;
                    }
                    return path;
                }
                string releaseStore = EnsureStore( _feeds.GetReleaseFeedFolder( m ), Facade.DefaultStoreUrl );
                string developStore = EnsureStore( _feeds.GetCIFeedFolder( m ), new Uri( releaseStore ) );
                string localStore = EnsureStore( _feeds.GetLocalFeedFolder( m ), new Uri( developStore ) );
                string current = _buildInfo.TargetLocal
                                    ? localStore
                                    : (_buildInfo.TargetDevelop
                                         ? developStore
                                         : releaseStore);
                environmentVariables.Add( ("CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", current) );
            }
            var filteredSolutions = FilterSolutions( _dependencyResult.Solutions );
            foreach( var s in filteredSolutions )
            {
                if( !StartBuilding( m, filteredSolutions,s ) ) return false;
                var gitFolder = s.Solution.GitFolder;
                if( !_buildInfo.AutoCommit && !gitFolder.CheckCleanCommit( m ) )
                {
                    m.Error( $"GitFolder {gitFolder.SubPath} must be commited." );
                    return false;
                }
                if( !s.UpdatePackageDependencies( m, GetDependentPackageVersion, _fileSystem, _buildInfo.AllowPackageDependenciesDowngrade ) ) return false;
                var result = gitFolder.Commit( m, "Global build commit." );
                if( !result.Success ) return false;

                bool buildRequired = true;
                SVersion v = GetTargetVersion( m, s );
                if( v == null )
                {
                    m.Info( $"Target Version is null. Skipping solution build." );
                    continue;
                }

                var commitSHA1 = s.Solution.GitFolder.HeadCommitSHA1;
                m.Info( $"Target Version = {v}" );
                if( !result.CommitCreated )
                {
                    var existingPackages = s.Solution.PublishedProjects.Select( p => (p.Name, _feeds.FindInAnyLocalFeeds( m, p.Name, v )) ).ToList();
                    if( existingPackages.All( e => e.Item2 ) )
                    {
                        if( !_buildInfo.RunUnitTests )
                        {
                            m.Info( $"Skipping this solution build since no tests must be done and all packages exist locally: {existingPackages.Select( e => e.Item1 ).Concatenate()}" );
                            continue;
                        }
                        if( _testRunMemory.HasBeenTested( m, commitSHA1 ) )
                        {
                            m.Info( $"Skipping this solution build since this has been already tested and all packages exist locally: {existingPackages.Select( e => e.Item1 ).Concatenate()}" );
                            continue;
                        }
                        buildRequired = false;
                    }
                    else m.Info( $"Packages to produce: {existingPackages.Where( e => !e.Item2 ).Select( e => e.Item1 ).Concatenate()}" );
                }

                Debug.Assert( buildRequired || (_buildInfo.RunUnitTests && !_testRunMemory.HasBeenTested( m, commitSHA1 )) );

                var path = gitFolder.FileSystem.GetFileInfo( s.Solution.SolutionFolderPath ).PhysicalPath;
                var args = "run --project CodeCakeBuilder -autointeraction";
                if( !buildRequired ) args += " -target=\"Unit-Testing\" -exclusive -IgnoreNoPackagesToProduce=Y";
                if( !_buildInfo.RunUnitTests ) args += " -RunUnitTests=N";

                if( !OnBuildStart( m, s, v ) ) return false;
                try
                {
                    if( !Run( m, path, "dotnet", args, environmentVariables ) )
                    {
                        OnBuildFailed( m, s, v );
                        return false;
                    }
                    if( _buildInfo.RunUnitTests )
                    {
                        _testRunMemory.SetTested( m, commitSHA1 );
                    }
                    if( !OnBuildSucceed( m, s, v ) ) return false;
                }
                catch( Exception ex )
                {
                    m.Error( $"Build failed.", ex );
                    OnBuildFailed( m, s, v );
                    return false;
                }
                if( !CopyReleasesPackagesToLocalFolder( m, s.Solution ) ) return false;
            }
            return true;
        }

        protected GlobalBuilderInfo BuildInfo => _buildInfo;

        protected ILocalFeedProvider Feeds => _feeds;

        protected string GetTargetFeedFolderPath( IActivityMonitor m )
        {
            return _buildInfo.TargetLocal
                        ? _feeds.GetLocalFeedFolder( m ).PhysicalPath
                        : (_buildInfo.TargetDevelop
                                ? _feeds.GetCIFeedFolder( m ).PhysicalPath
                                : _feeds.GetReleaseFeedFolder( m ).PhysicalPath);
        }

        protected virtual IReadOnlyList<SolutionDependencyResult.DependentSolution> FilterSolutions( IReadOnlyList<SolutionDependencyResult.DependentSolution> solutions )
        {
            return solutions;
        }

        protected virtual bool StartBuilding( IActivityMonitor m, IReadOnlyList<SolutionDependencyResult.DependentSolution> solutions, SolutionDependencyResult.DependentSolution s )
        {
            DisplaySolutionList( m, solutions, s );
            return true;
        }

        protected virtual SVersion GetDependentPackageVersion( IActivityMonitor m, string packageId )
        {
            return _feeds.GetBestAnyLocalVersion( m, packageId );
        }

        protected virtual SVersion GetTargetVersion( IActivityMonitor m, SolutionDependencyResult.DependentSolution s )
        {
            var info = s.Solution.GitFolder.ReadVersionInfo( m );
            return info.ContentOrFinalNuGetVersion;
        }

        protected virtual bool OnBuildStart( IActivityMonitor m, SolutionDependencyResult.DependentSolution s, SVersion v )
        {
            if( BuildInfo.WorkStatus == WorkStatus.SwitchingToDevelop )
            {
                // Coming from local, build needs to access the local feeds.
                // This should be the case but this corrects the files if needed.
                if( !s.Solution.GitFolder.EnsureLocalFeedsNuGetSource( m ).Success
                    || !s.Solution.GitFolder.SetRepositoryXmlIgnoreDirtyFolders( m ) )
                {
                    return false;
                }
            }
            return true;
        }

        protected virtual bool OnBuildSucceed( IActivityMonitor m, SolutionDependencyResult.DependentSolution s, SVersion v )
        {
            if( BuildInfo.WorkStatus == WorkStatus.SwitchingToDevelop && !s.Solution.GitFolder.ResetHard( m ) ) return false;
            return true;
        }

        protected virtual void OnBuildFailed( IActivityMonitor m, SolutionDependencyResult.DependentSolution s, SVersion v )
        {
            if( BuildInfo.WorkStatus == WorkStatus.SwitchingToDevelop ) s.Solution.GitFolder.ResetHard( m );
        }

        static void DisplaySolutionList( IActivityMonitor m, IReadOnlyList<SolutionDependencyResult.DependentSolution> all, SolutionDependencyResult.DependentSolution current = null )
        {
            using( m.OpenInfo( "Solutions to build:" ) )
            {
                int rank = -1;
                foreach( var s in all )
                {
                    if( rank != s.Rank )
                    {
                        rank = s.Rank;
                        m.Info( $" -- Rank {rank}" );
                    }
                    m.Info( $"{(s == current ? '*' : ' ')}   {s.Index} - {s.Solution} => {s.MinimalImpacts.Count}/{s.Impacts.Count}/{s.TransitiveImpacts.Count}" );
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

        bool CopyReleasesPackagesToLocalFolder( IActivityMonitor m, Solution solution )
        {
            using( m.OpenInfo( $"Copying released Packages to LocalFeed." ) )
            {
                try
                {
                    var targetFolder = GetTargetFeedFolderPath( m );
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

    }
}
