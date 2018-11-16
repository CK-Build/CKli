using CK.Core;
using CK.NuGetClient;
using CK.Text;
using CKSetup;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    public class WorldContext
    {
        static readonly XName xWorkStatus = XNamespace.None + "WorkStatus";

        readonly GlobalGitContext _globalGitContext;
        readonly IWorldStore _worldStore;
        readonly ILocalFeedProvider _feeds;
        readonly INuGetClient _nugetClient;
        readonly ISecretKeyStore _secretKeyStore;
        readonly Func<IActivityMonitor, string, IReadOnlyList<Solution>> _branchSolutionsLoader;
        readonly WorldState _worldState;
        readonly TestedCommitMemory _testedCommits;
        readonly GlobalBuilderInfo _buildInfo;
        WorkStatus _workStatus;

        class TestedCommitMemory : ITestRunMemory
        {
            readonly WorldContext _ctx;
            readonly HashSet<string> _testedCommitMemory;

            public TestedCommitMemory( WorldContext ctx )
            {
                _ctx = ctx;
                _testedCommitMemory = new HashSet<string>();
                string[] id = _ctx._worldState.XmlState.Element( "TestedCommitMemory" )?.Value.Split( '|' );
                if( id != null ) _testedCommitMemory.AddRangeArray( id );
                else _ctx._worldState.XmlState.Add( new XElement( "TestedCommitMemory" ) );
            }

            bool UpdateXmlState( IActivityMonitor m )
            {
                if( HasChanged )
                {
                    _ctx.SetState( m, state => state.XmlState.Element( "TestedCommitMemory" ).Value = _testedCommitMemory.Concatenate( "|" ) );
                    HasChanged = false;
                    return true;
                }
                return false;
            }

            public bool HasChanged { get; private set; }

            public bool HasBeenTested( IActivityMonitor m, string key )
            {
                return _testedCommitMemory.Contains( key );
            }

            public void SetTested( IActivityMonitor m, string key )
            {
                HasChanged |= _testedCommitMemory.Add( key );
                UpdateXmlState( m );
            }
        }

        WorldContext(
            GlobalGitContext git,
            IWorldStore store,
            ILocalFeedProvider feeds,
            INuGetClient nuGetClient,
            ISecretKeyStore secretKeyStore,
            WorkStatus wStatus,
            WorldState worldState,
            Func<IActivityMonitor, string, IReadOnlyList<Solution>> branchSolutionsLoader )
        {
            _globalGitContext = git;
            _worldStore = store;
            _feeds = feeds;
            _nugetClient = nuGetClient;
            _secretKeyStore = secretKeyStore;
            _worldState = worldState;
            _workStatus = wStatus;
            _branchSolutionsLoader = branchSolutionsLoader;
            _testedCommits = new TestedCommitMemory( this );
            _buildInfo = new GlobalBuilderInfo( secretKeyStore );
        }

        bool SetState( IActivityMonitor m, Action<WorldState> a ) => _worldStore.SetState( m, _worldState, a );

        bool SetState( IActivityMonitor m, StandardGitStatus gitStatus, Action<WorldState> a = null )
        {
            Action<WorldState> aS = state =>
            {
                state.GlobalGitStatus = gitStatus;
                a?.Invoke( state );
            };
            return _worldStore.SetState( m, _worldState, aS );
        }

        bool SetState( IActivityMonitor m, WorkStatus workStatus, Action<WorldState> a = null )
        {
            Action<WorldState> aW = state =>
            {
                state.XmlState.SetAttributeValue( xWorkStatus, workStatus );
                a?.Invoke( state );
            };
            if( !_worldStore.SetState( m, _worldState, aW ) ) return false;
            _workStatus = workStatus;
            return true;
        }

        bool SetState( IActivityMonitor m, WorkStatus workStatus, StandardGitStatus gitStatus, Action<WorldState> a = null )
        {
            Action<WorldState> aW = state =>
            {
                state.GlobalGitStatus = gitStatus;
                state.XmlState.SetAttributeValue( xWorkStatus, workStatus );
                a?.Invoke( state );
            };
            if( !_worldStore.SetState( m, _worldState, aW ) ) return false;
            _workStatus = workStatus;
            return true;
        }

        public IWorldName World => _globalGitContext.World;

        public FileSystem FileSystem => _globalGitContext.FileSystem;

        public StandardGitStatus GlobalGitStatus => _worldState.GlobalGitStatus;

        public WorkStatus WorkStatus => _workStatus;

        /// <summary>
        /// Gets the current, stable, branch name. This is null when <see cref="IsTransitioning"/> is true.
        /// </summary>
        public string CurrentBranchName => _worldState.GlobalGitStatus == StandardGitStatus.DevelopBranch
                                                ? World.DevelopBranchName
                                                : (_worldState.GlobalGitStatus == StandardGitStatus.LocalBranch
                                                      ? World.LocalBranchName
                                                      : null);

        public bool IsTransitioning => _workStatus == WorkStatus.SwitchingToLocal || _workStatus == WorkStatus.SwitchingToDevelop;

        /// <summary>
        /// Gets whether the <see cref="WorkStatus"/> is not <see cref="WorkStatus.Idle"/>.
        /// </summary>
        public bool HasWorkPending => _workStatus != WorkStatus.Idle;

        public bool ConcludeCurrentWork( IActivityMonitor m )
        {
            if( _workStatus == WorkStatus.SwitchingToDevelop )
            {
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchFromLocalToDevelop( m ) ) return false;
                }
                var r = GetSolutionDependencyResult( m, World.DevelopBranchName );
                if( r == null ) return false;

                _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
                var b = new GlobalBuilder( r, FileSystem, _feeds, _nugetClient, _testedCommits, _buildInfo );
                if( !b.Build( m ) ) return false;
                using( m.OpenInfo( $"Upgrading Build projects dependencies and removing LocalFeed NuGet sources." ) )
                {
                    foreach( var s in r.Solutions )
                    {
                        if( !s.UpdatePackageDependencies( m, _feeds.GetBestAnyLocalVersion, FileSystem, allowDowngrade: false, buildProjects: true ) ) return false;
                        var fNuGet = s.Solution.GetPlugin<NugetConfigFile>();
                        fNuGet.RemoveLocalFeeds( m );
                        if( !fNuGet.Save( m ) ) return false;
                        if( !s.Solution.GitFolder.Commit( m, "Updated Build projects and removed LocalFeed NuGet sources." ).Success ) return false;
                    }
                }
                if( !SetState( m, WorkStatus.Idle, StandardGitStatus.DevelopBranch ) ) return false;
            }
            else if( _workStatus == WorkStatus.SwitchingToLocal )
            {
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchFromDevelopToLocal( m, autoCommit:true, commitLocalChanges:false ) ) return false;
                }
                var r = GetSolutionDependencyResult( m, World.LocalBranchName );
                if( r == null ) return false;
                if( !DoLocalZeroBuildProjects( m, r, true ) ) return false;

                var rZeroBuildDeps = GetSolutionDependencyResult( m, World.LocalBranchName );
                if( rZeroBuildDeps == null ) return false;
                _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
                var b = new GlobalBuilder( rZeroBuildDeps, FileSystem, _feeds, _nugetClient, _testedCommits, _buildInfo );
                if( !b.Build( m ) ) return false;
                if( !SetState( m, WorkStatus.Idle, StandardGitStatus.LocalBranch ) ) return false;
            }
            else if( _workStatus == WorkStatus.Releasing )
            {
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchFromMasterToDevelop( m ) ) return false;
                }
                var r = GetSolutionDependencyResult( m, World.DevelopBranchName );
                if( r == null ) return false;
                var roadMap = GetSimpleRoadmap( m );
                if( roadMap == null ) return false;
                if( !DoRelease( m, r, roadMap ) ) return false;
            }
            else if( _workStatus == WorkStatus.CancellingRelease )
            {
                var roadmap = GetSimpleRoadmap( m );
                if( roadmap == null ) return false;
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchFromMasterToDevelop( m ) ) return false;
                }
                // CI Build (clearing version tags).
                var r = GetSolutionDependencyResult( m, World.DevelopBranchName );
                if( r == null ) return false;
                _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
                var b = new GlobalBuilderRelease( r, FileSystem, _feeds, _nugetClient, _testedCommits, _buildInfo, roadmap );
                if( !b.Build( m ) ) return false;

                // This should be in the SimpleRoadmap.
                var publishedPackages = roadmap.Where( e => e.Build )
                               .SelectMany( e => r.Solutions.First( s => s.Solution.UniqueSolutionName == e.SolutionName )
                                                   .Solution
                                                   .PublishedProjects
                                                   .Select( p => (PackageId: p.Name, e.ReleaseInfo.Version) ) )
                               .ToList();
                using( m.OpenInfo( $"Removing published components from Release local store." ) )
                {
                    using( var store = LocalStore.Open( m, _feeds.GetReleaseCKSetupStorePath( m ) ) )
                    {
                        if( store != null )
                        {
                            int removedCount = store.RemoveComponents( c => publishedPackages.Any( p => p.PackageId == c.Name && p.Version == c.Version ) );
                            m.Info( $"Removed {removedCount} components from Release local store." );
                            removedCount = store.GarbageCollectFiles();
                            m.Info( $"Removed {removedCount} files from Release local store." );
                        }
                    }
                }
                using( m.OpenInfo( $"Clearing locally published packages and NuGet local cache." ) )
                {
                    foreach( var p in publishedPackages )
                    {
                        _feeds.RemoveFromFeeds( m, p.PackageId, p.Version );
                        _feeds.RemoveFromNuGetCache( m, p.PackageId, p.Version );
                    }
                }
                using( m.OpenInfo( $"Upgrading Build projects dependencies to use CI builds." ) )
                {
                    foreach( var s in r.Solutions )
                    {
                        if( !s.UpdatePackageDependencies( m, _feeds.GetBestAnyLocalVersion, FileSystem, allowDowngrade: true, buildProjects: true ) ) return false;
                        if( !s.Solution.GitFolder.Commit( m, "Updated Build projects to use CI builds." ).Success ) return false;
                    }
                }
                if( !SetState( m, WorkStatus.Idle, StandardGitStatus.DevelopBranch ) ) return false;
            }
            else if( _workStatus == WorkStatus.PublishingRelease )
            {
                return DoPublishRelease( m );
            }

            m.Info( $"Work done. Current Status: {WorkStatus} / {GlobalGitStatus}." );
            return true;
        }

        SimpleRoadmap GetSimpleRoadmap( IActivityMonitor m )
        {
            var roadmap = _worldState.XmlState.Element( "RoadMap" );
            if( roadmap == null )
            {
                m.Error( "Unable to find the RoadMap element in WorldState XmlState." );
                return null;
            }
            int count = _worldState.XmlState.Elements( "RoadMap" ).Count();
            if( count > 1 )
            {
                m.Error( $"Found {count} RoadMap elements in WorldState XmlState. Only one must exist." );
                return null;
            }
            return new SimpleRoadmap( roadmap );
        }

        bool DoRelease( IActivityMonitor m, SolutionDependencyResult r, SimpleRoadmap roadmap )
        {
            Debug.Assert( _workStatus == WorkStatus.Releasing );
            _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
            var b = new GlobalBuilderRelease( r, FileSystem, _feeds, _nugetClient, _testedCommits, _buildInfo, roadmap );
            if( !b.Build( m ) ) return false;
            return SetState( m, WorkStatus.WaitingReleaseConfirmation );
        }

        public bool CanSwitchToDevelop => WorkStatus == WorkStatus.Idle && GlobalGitStatus == StandardGitStatus.LocalBranch;

        public bool SwitchToDevelop( IActivityMonitor m )
        {
            if( !CanSwitchToDevelop ) throw new InvalidOperationException( nameof(CanSwitchToDevelop) );
            return SetState( m, WorkStatus.SwitchingToDevelop ) && ConcludeCurrentWork( m );
        }

        public bool CanSwitchToLocal => WorkStatus == WorkStatus.Idle && GlobalGitStatus == StandardGitStatus.DevelopBranch;

        public bool SwitchToLocal( IActivityMonitor m )
        {
            if( !CanSwitchToLocal ) throw new InvalidOperationException( nameof( CanSwitchToLocal ) );
            return SetState( m, WorkStatus.SwitchingToLocal ) && ConcludeCurrentWork( m );
        }

        public bool CanRunCIBuild => WorkStatus == WorkStatus.Idle;

        /// <summary>
        /// Runs a CI build.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool RunCIBuild( IActivityMonitor m )
        {
            if( !CanRunCIBuild ) throw new InvalidOperationException( nameof( CanRunCIBuild ) );
            _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
            var r = GetSolutionDependencyResult( m, GlobalGitStatus == StandardGitStatus.LocalBranch
                                                        ? World.LocalBranchName
                                                        : World.DevelopBranchName );
            if( r == null ) return false;
            var b = new GlobalBuilder( r, FileSystem, _feeds, _nugetClient, _testedCommits, _buildInfo );
            return b.Build( m );
        }

        /// <summary>
        /// Gets whether <see cref="CreateSolutionDependencyResult"/> can be called.
        /// <see cref="WorkStatus"/> must be <see cref="WorkStatus.Idle"/>.
        /// </summary>
        public bool CanCreateSolutionDependencyResult => WorkStatus == WorkStatus.Idle;

        /// <summary>
        /// Creates a new <see cref="SolutionDependencyResult"/> based on <see cref="CurrentBranchName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The solution result. Can be null on error.</returns>
        public SolutionDependencyResult CreateSolutionDependencyResult( IActivityMonitor m )
        {
            if( !CanCreateSolutionDependencyResult ) throw new InvalidOperationException( nameof( CanCreateSolutionDependencyResult ) );
            return GetSolutionDependencyResult( m, CurrentBranchName );
        }

        public bool CanLocalFixToZeroBuildProjects => WorkStatus == WorkStatus.Idle && GlobalGitStatus == StandardGitStatus.LocalBranch;

        public bool LocalFixToZeroBuildProjects( IActivityMonitor m, bool commitChanges = false )
        {
            if( !CanLocalFixToZeroBuildProjects ) throw new InvalidOperationException( nameof( CanLocalFixToZeroBuildProjects ) );
            var r = GetSolutionDependencyResult( m, World.LocalBranchName );
            if( r == null ) return false;
            return DoLocalZeroBuildProjects( m, r, commitChanges );
        }

        bool DoLocalZeroBuildProjects( IActivityMonitor m, SolutionDependencyResult r, bool commitChanges )
        {
            var buildDepNames = r.BuildProjectsInfo.DependenciesToBuild.Select( p => p.Project.Project.Name );

            using( m.OpenInfo( $"Removing {buildDepNames.Concatenate()} packages ZeroVersion from local NuGet cache if they exist." ) )
            {
                foreach( var pName in buildDepNames )
                {
                    _feeds.RemoveFromNuGetCache( m, pName, SVersion.ZeroVersion );
                }
            }

            var touchedSolutions = new List<Solution>();
            using( m.OpenInfo( $"Updating package references to ZeroVersion." ) )
            {
                foreach( var sp in r.BuildProjectsInfo.ProjectsToUpgrade.GroupBy( p => p.Project.Project.PrimarySolution ) )
                {
                    touchedSolutions.Add( sp.Key );
                    foreach( var projectAndDeps in sp )
                    {
                        var project = projectAndDeps.Project.Project;
                        foreach( var dep in projectAndDeps.Packages )
                        {
                            project.SetPackageReferenceVersion( m, project.TargetFrameworks, dep.PackageId, SVersion.ZeroVersion );
                        }
                    }
                    if( !sp.Key.Save( m, FileSystem ) ) return false;
                }
            }

            using( m.OpenInfo( $"Generating ZeroVersion in LocalFeed/Local feed for packages: {buildDepNames.Concatenate()}" ) )
            {
                var args = $@"pack --output ""{_feeds.GetLocalFeedFolder( m ).PhysicalPath}"" --include-symbols --configuration Debug /p:Version=""{SVersion.ZeroVersion}"" /p:AssemblyVersion=""{InformationalVersion.ZeroAssemblyVersion}"" /p:FileVersion=""{InformationalVersion.ZeroFileVersion}"" /p:InformationalVersion=""{InformationalVersion.ZeroInformationalVersion}"" ";
                foreach( var p in r.BuildProjectsInfo.DependenciesToBuild )
                {
                    var path = FileSystem.GetFileInfo( p.Project.Project.Path.RemoveLastPart() ).PhysicalPath;
                    FileSystem.RawDeleteLocalDirectory( m, Path.Combine( path, "bin" ) );
                    FileSystem.RawDeleteLocalDirectory( m, Path.Combine( path, "obj" ) );
                    if( !GlobalBuilder.Run( m, path, "dotnet", args ) ) return false;
                }
            }

            // This appeared to be required once the ZeroVersions are avalaible otherwise
            // updated dependencies are ignored.
            using( m.OpenInfo( "Forcing a dotnet restore --force on all touched solutions." ) )
            {
                foreach( var s in touchedSolutions )
                {
                    var path = FileSystem.GetFileInfo( s.SolutionFolderPath ).PhysicalPath;
                    if( !GlobalBuilder.Run( m, path, "dotnet", "restore --force" ) ) return false;
                }
            }

            foreach( var s in r.Solutions )
            {
                if( commitChanges && !s.Solution.GitFolder.Commit( m, $"Build project now uses ZeroVersion available dependencies in LocalFeed/Local feed." ).Success )
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanRelease => WorkStatus == WorkStatus.Idle && GlobalGitStatus == StandardGitStatus.DevelopBranch;

        /// <summary>
        /// Starts a release after an optional <see cref="GitFolder.PullAll"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="pull">Pull all branches first.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Release( IActivityMonitor m, IReleaseVersionSelector versionSelector, bool pull = true )
        {
            if( !CanRelease ) throw new InvalidOperationException( nameof( CanRelease ) );
            var gitInfos = new Dictionary<GitFolder, RepositoryInfo>();
            foreach( var g in _globalGitContext.GitFolders )
            {
                if( !g.CheckCleanCommit( m ) ) return false;
                if( pull )
                {
                    if( !g.CheckoutAndPull( m, World.DevelopBranchName, true ).Success ) return false;
                }
                var info = g.ReadVersionInfo( m, World.DevelopBranchName );
                if( info == null ) return false;
                gitInfos.Add( g, info );
            }
            var r = GetSolutionDependencyResult( m, World.DevelopBranchName );
            if( r == null ) return false;
            var releasers = r.Solutions.Select( s => new SolutionReleaser( s, gitInfos[s.Solution.GitFolder] ) ).ToList();
            var releaser = new GlobalReleaser( releasers );
            XElement roadmap = releaser.ComputeFullRoadMap( m, versionSelector );
            if( roadmap == null ) return false;
            if( !SetState( m, WorkStatus.Releasing, state =>
            {
                state.XmlState.Element( "RoadMap" )?.Remove();
                state.XmlState.Add( roadmap );
            } ) )
            {
                return false;
            }
            return DoRelease( m, r, new SimpleRoadmap( roadmap ) );
        }

        /// <summary>
        /// Gets whether <see cref="CancelRelease"/> can be called.
        /// </summary>
        public bool CanCancelRelease => WorkStatus == WorkStatus.Releasing || WorkStatus == WorkStatus.WaitingReleaseConfirmation;

        /// <summary>
        /// Cancel the current release.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool CancelRelease( IActivityMonitor m )
        {
            if( !CanCancelRelease ) throw new InvalidOperationException( nameof( CanCancelRelease ) );
            return SetState( m, WorkStatus.CancellingRelease ) && ConcludeCurrentWork( m );
        }

        public bool CanPublishRelease => WorkStatus == WorkStatus.WaitingReleaseConfirmation;

        public bool PublishRelease( IActivityMonitor m )
        {
            if( !CanPublishRelease ) throw new InvalidOperationException( nameof( CanPublishRelease ) );
            return DoPublishRelease( m );
        }

        bool DoPublishRelease( IActivityMonitor m )
        {
            string storeApiKey = _secretKeyStore.GetCKSetupRemoteStorePushKey( m ).Secret;
            if( storeApiKey == null ) return false;
            var roadmap = GetSimpleRoadmap( m );
            if( roadmap == null ) return false;

            // Collects all packages that must be pushed: all packages in LocalFeed/Releases and
            // all CI dependencies of build projects.
            var toPush = new HashSet<LocalNuGetPackageFile>( _feeds.GetAllPackageFilesInReleaseFeed( m ) );
            bool hasError = false;
            foreach( var p in toPush.Where( p => !p.Version.IsValid || p.Version.AsCSVersion == null ) )
            {
                m.Error( $"Invalid version {p.Version} for package {p.PackageId} in release feed. Release feed can contain only valid CSemVer versions." );
                hasError = true;
            }
            if( hasError ) return false;

            // Gets all the solutions.
            IReadOnlyList<Solution> solutions = _branchSolutionsLoader( m, World.DevelopBranchName );
            if( solutions.Any( s => s == null ) ) return false;

            // Adds CI builds required for Build projects.
            // The list of build dependencies packages should be in the roadmap/XmlState.
            foreach( var buildDep in solutions.SelectMany( s => s.BuildProjects )
                                      .SelectMany( b => b.Deps.Packages.Where( d => d.Version.AsCSVersion == null ) ) )
            {
                var local = _feeds.GetLocalCIPackage( m, buildDep.PackageId, buildDep.Version );
                if( local == null )
                {
                    m.Error( $"Unable to find local package in {_feeds.GetCIFeedFolder( m ).PhysicalPath} for {buildDep}." );
                    hasError = true;
                }
                else toPush.Add( local );
            }
            if( hasError ) return false;

            var feedPackages = solutions
                                  .Select( s => (
                                        Feeds: s.Settings.NuGetPushFeeds.Select( info => _nugetClient.FindOrCreate( info ) ), 
                                        Packages: toPush.Where( t => s.PublishedProjects.Any( p => t.PackageId == p.Name ) ).ToList() ) )
                                  .Aggregate(
                                        new Dictionary<INuGetFeed,List<LocalNuGetPackageFile>>(),
                                        (accumulator, x) =>
                                        {
                                            foreach( var f in x.Feeds )
                                            {
                                                if( !accumulator.TryGetValue( f, out var packages ) )
                                                {
                                                    packages = new List<LocalNuGetPackageFile>();
                                                    accumulator.Add( f, packages );
                                                }
                                                packages.AddRange( x.Packages );
                                            }
                                            return accumulator;
                                        } );

            if( feedPackages.Count == 0 )
            {
                m.Error( $"No NuGet push feed found for solutions." );
                return false;
            }
            foreach( var f in feedPackages )
            {
                if( f.Key.ResolveSecret( m ) == null )
                {
                    m.Error( $"Unable to acquire required secret required to push NuGet packages." );
                    return false;
                }
            }

            if( WorkStatus == WorkStatus.WaitingReleaseConfirmation && !SetState( m, WorkStatus.PublishingRelease ) ) return false;

            // Push LocalFeed/Release/RemoteStore
            string storePath = _feeds.GetReleaseCKSetupStorePath( m );
            using( LocalStore store = LocalStore.OpenOrCreate( m, storePath ) )
            {
                if( store == null || !store.PushComponents( comp => true, Facade.DefaultStoreUrl, storeApiKey ) )
                {
                    return false;
                }
            }
            // Push packages to their respective feeds.
            foreach( var f in feedPackages )
            {
                using( m.OpenInfo( $"Pushing {f.Value.Count} packages to {f.Key.Info.Name}." ) )
                {
                    f.Key.PushPackagesAsync( m, f.Value ).GetAwaiter().GetResult();
                }
            }

            // Push release tags, masters (for Official Release) and develops branches.
            foreach( var g in _globalGitContext.GitFolders )
            {
                var v = roadmap.FirstOrDefault( e => e.Build && e.SolutionName == g.SubPath.LastPart )
                               ?.ReleaseInfo.Version;
                if( v != null )
                {
                    if( !g.PushVersionTag( m, v ) ) return false;
                    if( v.Prerelease.Length == 0 )
                    {
                        if( !g.Push( m, World.MasterBranchName ) ) return false;
                    }
                    if( !g.Push( m, World.DevelopBranchName ) ) return false;
                }
                else
                {
                    m.Warn( $"No version found in roadmap for GitFolder {g.SubPath}. Push is skipped." );
                }
            }
            return SetState( m, WorkStatus.Idle );
        }

        SolutionDependencyResult GetSolutionDependencyResult( IActivityMonitor m, string branchName )
        {
            IReadOnlyList<Solution> solutions = _branchSolutionsLoader( m, branchName );
            if( solutions.Any( s => s == null ) ) return null;

            var deps = DependencyContext.Create( m, solutions );
            if( deps == null ) return null;
            SolutionDependencyResult r = deps.AnalyzeDependencies( m, SolutionSortStrategy.EverythingExceptBuildProjects );
            if( r.HasError )
            {
                r.RawSolutionSorterResult.LogError( m );
                return null;
            }
            return r;
        }

        public static WorldContext Create(
            IActivityMonitor m,
            IWorldName world,
            IWorldStore worldStore,
            ILocalFeedProvider feeds,
            INuGetClient nugetClient,
            ISecretKeyStore secretKeyStore,
            IEnumerable<GitFolder> gitFolders,
            Func<IActivityMonitor, string, IReadOnlyList<Solution>> branchSolutionsLoader )
        {
            var gitContext = new GlobalGitContext( world, gitFolders );
            var worldState = worldStore.GetOrCreateLocalState( m, world );
            var workStatus = worldState.XmlState.AttributeEnum( xWorkStatus, WorkStatus.Idle );
            var gitStatus = worldState.GlobalGitStatus;
            if( !gitContext.CheckStatus( m, ref gitStatus, workStatus == WorkStatus.SwitchingToDevelop || workStatus == WorkStatus.SwitchingToLocal ) ) return null;
            Debug.Assert( gitStatus != StandardGitStatus.Unknwon );
            worldState.GlobalGitStatus = gitStatus;
            return new WorldContext( gitContext, worldStore, feeds, nugetClient, secretKeyStore, workStatus, worldState, branchSolutionsLoader );
        }

    }
}
