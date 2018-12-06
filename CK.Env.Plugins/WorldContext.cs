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
    public class WorldContext : ICommandMethodsProvider
    {
        readonly GlobalGitContext _globalGitContext;
        readonly IWorldStore _worldStore;
        readonly ILocalFeedProvider _feeds;
        readonly INuGetClient _nugetClient;
        readonly ISecretKeyStore _secretKeyStore;
        readonly Func<IActivityMonitor, string, IReadOnlyList<Solution>> _branchSolutionsLoader;
        readonly RawXmlWorldState _worldState;
        readonly TestedCommitMemory _testedCommits;
        readonly GlobalBuilderInfo _buildInfo;

        class TestedCommitMemory : ITestRunMemory
        {
            readonly WorldContext _ctx;
            readonly HashSet<string> _testedCommitMemory;

            public TestedCommitMemory( WorldContext ctx )
            {
                _ctx = ctx;
                _testedCommitMemory = new HashSet<string>();
                string[] id = _ctx._worldState.GeneralState.Element( "TestedCommitMemory" )?.Value.Split( '|' );
                if( id != null ) _testedCommitMemory.AddRangeArray( id );
                else _ctx._worldState.GeneralState.Add( new XElement( "TestedCommitMemory" ) );
            }

            bool UpdateXmlState( IActivityMonitor m )
            {
                if( HasChanged )
                {
                    _ctx.SetState( m, state => state.GeneralState.Element( "TestedCommitMemory" ).Value = _testedCommitMemory.Concatenate( "|" ) );
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
            RawXmlWorldState worldState,
            Func<IActivityMonitor, string, IReadOnlyList<Solution>> branchSolutionsLoader )
        {
            _globalGitContext = git;
            _worldStore = store;
            _feeds = feeds;
            _nugetClient = nuGetClient;
            _secretKeyStore = secretKeyStore;
            _worldState = worldState;
            _branchSolutionsLoader = branchSolutionsLoader;
            _testedCommits = new TestedCommitMemory( this );
            _buildInfo = new GlobalBuilderInfo( secretKeyStore );
            CommandProviderName = "WorldContext";
        }

        public NormalizedPath CommandProviderName { get; }

        bool SetState( IActivityMonitor m, Action<RawXmlWorldState> a ) => _worldStore.SetState( m, _worldState, a );

        bool SetState( IActivityMonitor m, GlobalWorkStatus workStatus, Action<RawXmlWorldState> a = null )
        {
            Action<RawXmlWorldState> aW = state =>
            {
                //state.WorkStatus = workStatus;
                a?.Invoke( state );
            };
            if( !_worldStore.SetState( m, _worldState, aW ) ) return false;
            return true;
        }

        bool SetState( IActivityMonitor m, GlobalWorkStatus workStatus, StandardGitStatus gitStatus, Action<RawXmlWorldState> a = null )
        {
            Action<RawXmlWorldState> aW = state =>
            {
                //state.GlobalGitStatus = gitStatus;
                //state.WorkStatus = workStatus;
                a?.Invoke( state );
            };
            if( !_worldStore.SetState( m, _worldState, aW ) ) return false;
            return true;
        }

        public IWorldName World => _globalGitContext.World;

        public FileSystem FileSystem => _globalGitContext.FileSystem;

        public StandardGitStatus GlobalGitStatus => throw new NotImplementedException(); // _worldState.GlobalGitStatus;

        public GlobalWorkStatus WorkStatus => _worldState.WorkStatus;

        ///// <summary>
        ///// Gets the current, stable, branch name. This is null when <see cref="IsTransitioning"/> is true.
        ///// </summary>
        //public string CurrentBranchName => _worldState.GlobalGitStatus == StandardGitStatus.DevelopBranch
        //                                        ? World.DevelopBranchName
        //                                        : (_worldState.GlobalGitStatus == StandardGitStatus.LocalBranch
        //                                              ? World.LocalBranchName
        //                                              : null);

        public bool IsTransitioning => WorkStatus == GlobalWorkStatus.SwitchingToLocal || WorkStatus == GlobalWorkStatus.SwitchingToDevelop;

        /// <summary>
        /// Gets whether the <see cref="WorkStatus"/> is not <see cref="GlobalWorkStatus.Idle"/>.
        /// </summary>
        public bool IsConcludeCurrentWorkEnabled => WorkStatus != GlobalWorkStatus.Idle;

        [CommandMethod]
        public bool ConcludeCurrentWork( IActivityMonitor m )
        {
            if( WorkStatus == GlobalWorkStatus.SwitchingToDevelop )
            {
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchLocalToDevelop( m ) ) return false;
                }
                var r = GetSolutionDependencyContext( m, World.DevelopBranchName );
                if( r == null ) return false;

                _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
                var b = new GlobalBuilder( r, FileSystem, _feeds, _nugetClient, _testedCommits, _buildInfo );
                if( !b.Build( m ) ) return false;
                using( m.OpenInfo( $"Upgrading Build projects dependencies and removing LocalFeed NuGet sources." ) )
                {
                    foreach( var s in r.Solutions )
                    {
                        if( !s.UpdatePackageDependencies( m, _feeds.GetBestAnyLocalVersion, allowDowngrade: false, buildProjects: true ) ) return false;
                        var fNuGet = s.Solution.GetPlugin<CK.Env.Plugins.SolutionFiles.NugetConfigFile>();
                        fNuGet.RemoveLocalFeeds( m );
                        if( !fNuGet.Save( m ) ) return false;
                        if( !s.Solution.GitFolder.Commit( m, "Updated Build projects and removed LocalFeed NuGet sources." ).Success ) return false;
                    }
                }
                if( !SetState( m, GlobalWorkStatus.Idle, StandardGitStatus.DevelopBranch ) ) return false;
            }
            else if( WorkStatus == GlobalWorkStatus.SwitchingToLocal )
            {
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchDevelopToLocal( m, autoCommit:true ) ) return false;
                }
                var r = GetSolutionDependencyContext( m, World.LocalBranchName );
                if( r == null ) return false;
                if( !DoLocalZeroBuildProjects( m, r, true ) ) return false;

                var rZeroBuildDeps = GetSolutionDependencyContext( m, World.LocalBranchName );
                if( rZeroBuildDeps == null ) return false;
                _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
                var b = new GlobalBuilder( rZeroBuildDeps, FileSystem, _feeds, _nugetClient, _testedCommits, _buildInfo );
                if( !b.Build( m ) ) return false;
                if( !SetState( m, GlobalWorkStatus.Idle, StandardGitStatus.LocalBranch ) ) return false;
            }
            else if( WorkStatus == GlobalWorkStatus.Releasing )
            {
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchMasterToDevelop( m ) ) return false;
                }
                var r = GetSolutionDependencyContext( m, World.DevelopBranchName );
                if( r == null ) return false;
                var roadMap = GetSimpleRoadmap( m );
                if( roadMap == null ) return false;
                if( !DoRelease( m, r, roadMap ) ) return false;
            }
            else if( WorkStatus == GlobalWorkStatus.CancellingRelease )
            {
                var roadmap = GetSimpleRoadmap( m );
                if( roadmap == null ) return false;
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchMasterToDevelop( m ) ) return false;
                }
                // CI Build (clearing version tags).
                var r = GetSolutionDependencyContext( m, World.DevelopBranchName );
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
                        if( !s.UpdatePackageDependencies( m, _feeds.GetBestAnyLocalVersion, allowDowngrade: true, buildProjects: true ) ) return false;
                        if( !s.Solution.GitFolder.Commit( m, "Updated Build projects to use CI builds." ).Success ) return false;
                    }
                }
                if( !SetState( m, GlobalWorkStatus.Idle, StandardGitStatus.DevelopBranch ) ) return false;
            }
            else if( WorkStatus == GlobalWorkStatus.PublishingRelease )
            {
                return DoPublishRelease( m );
            }

            m.Info( $"Work done. Current Status: {WorkStatus} / {GlobalGitStatus}." );
            return true;
        }

        SimpleRoadmap GetSimpleRoadmap( IActivityMonitor m )
        {
            var roadmap = _worldState.GeneralState.Element( "RoadMap" );
            if( roadmap == null )
            {
                m.Error( "Unable to find the RoadMap element in WorldState XmlState." );
                return null;
            }
            int count = _worldState.GeneralState.Elements( "RoadMap" ).Count();
            if( count > 1 )
            {
                m.Error( $"Found {count} RoadMap elements in WorldState XmlState. Only one must exist." );
                return null;
            }
            return new SimpleRoadmap( roadmap );
        }

        bool DoRelease( IActivityMonitor m, SolutionDependencyContext r, SimpleRoadmap roadmap )
        {
            Debug.Assert( WorkStatus == GlobalWorkStatus.Releasing );
            _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
            var b = new GlobalBuilderRelease( r, FileSystem, _feeds, _nugetClient, _testedCommits, _buildInfo, roadmap );
            if( !b.Build( m ) ) return false;
            return SetState( m, GlobalWorkStatus.WaitingReleaseConfirmation );
        }

        public bool IsOrderedPushDevelopEnabled => WorkStatus == GlobalWorkStatus.Idle && GlobalGitStatus == StandardGitStatus.DevelopBranch;

        [CommandMethod]
        public bool OrderedCommitAndPushOnDevelop( IActivityMonitor m, string commitMessage, int sleepTimeSeconds = 5 )
        {
            if( !IsOrderedPushDevelopEnabled ) throw new InvalidOperationException( nameof( IsOrderedPushDevelopEnabled ) );
            if( String.IsNullOrWhiteSpace( commitMessage ) ) throw new ArgumentNullException( nameof( commitMessage ) );
            // Secondary solutions are in the set. Handle GitGolder only once. 
            var r = GetSolutionDependencyContext( m, World.DevelopBranchName );
            if( r == null ) return false;
            foreach( var g in r.Solutions.Select( s => s.Solution.GitFolder ).Distinct() )
            {
                var commit = g.Commit( m, commitMessage );
                if( !commit.Success ) return false;
                if( commit.CommitCreated )
                {
                    if( !g.Push( m ) ) return false;
                    System.Threading.Thread.Sleep( sleepTimeSeconds * 1000 );
                }
            }
            return true;
        }

        public bool CanSwitchToDevelop => WorkStatus == GlobalWorkStatus.Idle && GlobalGitStatus == StandardGitStatus.LocalBranch;

        [CommandMethod]
        public bool SwitchToDevelop( IActivityMonitor m )
        {
            if( !CanSwitchToDevelop ) throw new InvalidOperationException( nameof(CanSwitchToDevelop) );
            return SetState( m, GlobalWorkStatus.SwitchingToDevelop ) && ConcludeCurrentWork( m );
        }

        public bool CanSwitchToLocal => WorkStatus == GlobalWorkStatus.Idle && GlobalGitStatus == StandardGitStatus.DevelopBranch;

        [CommandMethod]
        public bool SwitchToLocal( IActivityMonitor m )
        {
            if( !CanSwitchToLocal ) throw new InvalidOperationException( nameof( CanSwitchToLocal ) );
            return SetState( m, GlobalWorkStatus.SwitchingToLocal ) && ConcludeCurrentWork( m );
        }

        /// <summary>
        /// Gets whether <see cref="RunCIBuild"/> can be called.
        /// </summary>
        public bool CanRunCIBuild => WorkStatus == GlobalWorkStatus.Idle;

        /// <summary>
        /// Runs a CI build.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool RunCIBuild( IActivityMonitor m )
        {
            if( !CanRunCIBuild ) throw new InvalidOperationException( nameof( CanRunCIBuild ) );
            _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
            var r = GetSolutionDependencyContext( m, GlobalGitStatus == StandardGitStatus.LocalBranch
                                                        ? World.LocalBranchName
                                                        : World.DevelopBranchName );
            if( r == null ) return false;
            var b = new GlobalBuilder( r, FileSystem, _feeds, _nugetClient, _testedCommits, _buildInfo );
            return b.Build( m );
        }

        /// <summary>
        /// Gets whether <see cref="CreateSolutionDependencyContext"/> can be called.
        /// <see cref="WorkStatus"/> must be <see cref="GlobalWorkStatus.Idle"/>.
        /// </summary>
        public bool CanCreateSolutionDependencyContext => WorkStatus == GlobalWorkStatus.Idle;

        /// <summary>
        /// Creates a new <see cref="SolutionDependencyContext"/> based on <see cref="CurrentBranchName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The solution result. Can be null on error.</returns>
        public SolutionDependencyContext CreateSolutionDependencyContext( IActivityMonitor m )
        {
            if( !CanCreateSolutionDependencyContext ) throw new InvalidOperationException( nameof( CanCreateSolutionDependencyContext ) );
            return null;// GetSolutionDependencyContext( m, CurrentBranchName );
        }

        public bool CanLocalFixToZeroBuildProjects => WorkStatus == GlobalWorkStatus.Idle
                                                      && GlobalGitStatus == StandardGitStatus.LocalBranch;

        [CommandMethod]
        public bool LocalFixToZeroBuildProjects( IActivityMonitor m )
        {
            if( !CanLocalFixToZeroBuildProjects ) throw new InvalidOperationException( nameof( CanLocalFixToZeroBuildProjects ) );
            var r = GetSolutionDependencyContext( m, World.LocalBranchName );
            if( r == null ) return false;
            return DoLocalZeroBuildProjects( m, r, false );
        }

        bool DoLocalZeroBuildProjects( IActivityMonitor m, SolutionDependencyContext r, bool commitChanges )
        {
            var buildDepNames = r.BuildProjectsInfo.DependenciesToBuild.Select( p => p.Project.Project.Name );
            var buildDepNamesText = buildDepNames.Concatenate();

            var buildProjectsZeroVersionSHA1Signature = r.BuildProjectsInfo
                                                         .DependenciesToBuild
                                                         .Select( rp => rp.Project.Project.Solution.GitFolder.HeadCommitSHA1 )
                                                         .Concatenate( "|" );

            if( buildProjectsZeroVersionSHA1Signature == _worldState.GeneralState.Element( "BuildProjectsZeroVersionSHA1Signature" )?.Value )
            {
                m.Info( $"Build project signature match. ZeroVersion Packages are already built and propagated (Build Projects: {buildDepNames})." );
                return true;
            }

            using( m.OpenInfo( $"Removing {buildDepNamesText} packages ZeroVersion from local NuGet cache if they exist." ) )
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
                    if( !sp.Key.Save( m ) ) return false;
                }
            }

            using( m.OpenInfo( $"Generating ZeroVersion in LocalFeed/Local feed for packages: {buildDepNamesText}" ) )
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

            return SetState( m, state => state.GeneralState.SetElementValue( "BuildProjectsZeroVersionSHA1Signature", buildProjectsZeroVersionSHA1Signature ) );
       }

        public bool CanRelease => WorkStatus == GlobalWorkStatus.Idle && GlobalGitStatus == StandardGitStatus.DevelopBranch;

        /// <summary>
        /// Starts a release after an optional <see cref="GitFolder.PullAllBranches"/>.
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
                var info = g.ReadRepositoryVersionInfo( m, World.DevelopBranchName );
                if( info == null ) return false;
                gitInfos.Add( g, info );
            }
            var r = GetSolutionDependencyContext( m, World.DevelopBranchName );
            if( r == null ) return false;
            var releasers = r.Solutions.Select( s => new SolutionReleaser( s, gitInfos[s.Solution.GitFolder] ) ).ToList();
            var releaser = new GlobalReleaser( releasers );
            XElement roadmap = releaser.ComputeFullRoadMap( m, versionSelector );
            if( roadmap == null ) return false;
            if( !SetState( m, GlobalWorkStatus.Releasing, state =>
            {
                state.GeneralState.Element( "RoadMap" )?.Remove();
                state.GeneralState.Add( roadmap );
            } ) )
            {
                return false;
            }
            return DoRelease( m, r, new SimpleRoadmap( roadmap ) );
        }

        /// <summary>
        /// Gets whether <see cref="CancelRelease"/> can be called.
        /// </summary>
        public bool CanCancelRelease => WorkStatus == GlobalWorkStatus.Releasing || WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation;

        /// <summary>
        /// Cancel the current release.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool CancelRelease( IActivityMonitor m )
        {
            if( !CanCancelRelease ) throw new InvalidOperationException( nameof( CanCancelRelease ) );
            return SetState( m, GlobalWorkStatus.CancellingRelease ) && ConcludeCurrentWork( m );
        }

        public bool CanPublishRelease => WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation;

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

            if( WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation && !SetState( m, GlobalWorkStatus.PublishingRelease ) ) return false;

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
            return SetState( m, GlobalWorkStatus.Idle );
        }

        SolutionDependencyContext GetSolutionDependencyContext( IActivityMonitor m, string branchName )
        {
            using( m.OpenInfo( $"Computing SolutionDependencyContext for branch {branchName}." ) )
            {
                m.MinimalFilter = LogFilter.Terse;
                IReadOnlyList<Solution> solutions = _branchSolutionsLoader( m, branchName );
                if( solutions.Any( s => s == null ) ) return null;

                var deps = DependencyAnalyser.Create( m, solutions );
                if( deps == null ) return null;
                SolutionDependencyContext r = deps.CreateDependencyContext( m, SolutionSortStrategy.EverythingExceptBuildProjects );
                if( r.HasError )
                {
                    r.RawSolutionSorterResult.LogError( m );
                    return null;
                }
                return r;
            }
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
            if( !gitFolders.All( g => g.EnsureCurrentBranchPlugins( m ) ) )
            {
                return null;
            }
            var gitContext = new GlobalGitContext( world, gitFolders );
            var worldState = worldStore.GetOrCreateLocalState( m, world );
            StandardGitStatus gitStatus = StandardGitStatus.Unknwon;
            if( !gitContext.CheckStatus( m, ref gitStatus, worldState.WorkStatus == GlobalWorkStatus.SwitchingToDevelop
                                                           || worldState.WorkStatus == GlobalWorkStatus.SwitchingToLocal ) )
            {
                return null;
            }
            Debug.Assert( gitStatus != StandardGitStatus.Unknwon );
            return new WorldContext( gitContext, worldStore, feeds, nugetClient, secretKeyStore, worldState, branchSolutionsLoader );
        }

    }
}
