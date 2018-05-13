using CK.Core;
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
        readonly IFileProvider _referential;
        readonly IPublishKeyStore _publishKeyStore;
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
            IFileProvider referential,
            IPublishKeyStore publishKeyStore,
            WorkStatus wStatus,
            WorldState worldState,
            Func<IActivityMonitor, string, IReadOnlyList<Solution>> branchSolutionsLoader )
        {
            _globalGitContext = git;
            _worldStore = store;
            _feeds = feeds;
            _referential = referential;
            _publishKeyStore = publishKeyStore;
            _worldState = worldState;
            _workStatus = wStatus;
            _branchSolutionsLoader = branchSolutionsLoader;
            _testedCommits = new TestedCommitMemory( this );
            _buildInfo = new GlobalBuilderInfo( publishKeyStore );
        }

        bool SetState( IActivityMonitor m, Action<WorldState> a ) => _worldStore.SetState( m, _worldState, a );

        bool SetState( IActivityMonitor m, GlobalGitStatus gitStatus, Action<WorldState> a = null )
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

        bool SetState( IActivityMonitor m, WorkStatus workStatus, GlobalGitStatus gitStatus, Action<WorldState> a = null )
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

        public GlobalGitStatus GlobalGitStatus => _worldState.GlobalGitStatus;

        public WorkStatus WorkStatus => _workStatus;

        /// <summary>
        /// Gets the current, stable, branch name. This is null when <see cref="IsTransitioning"/> is true.
        /// </summary>
        public string CurrentBranchName => _worldState.GlobalGitStatus == GlobalGitStatus.DevelopBranch
                                                ? World.DevelopBranchName
                                                : (_worldState.GlobalGitStatus == GlobalGitStatus.LocalBranch
                                                      ? World.LocalBranchName
                                                      : null);

        public bool IsTransitioning => _workStatus == WorkStatus.SwitchingToLocal || _workStatus == WorkStatus.SwitchingToDevelop;

        public bool HasWorkPending => _workStatus != WorkStatus.Idle;

        public bool ConcludeCurrentWork( IActivityMonitor m )
        {
            if( _workStatus == WorkStatus.SwitchingToDevelop )
            {
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchFromLocalToDevelop( m ) ) return false;
                    // Coming from local, build needs to access the local feeds.
                    // This should be the case but this corrects the files if needed.
                    g.EnsureLocalFeedsNuGetSource( m );
                }
                var r = GetSolutionDependencyResult( m, World.DevelopBranchName );
                if( r == null ) return false;
                _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
                var b = new GlobalBuilder( r, FileSystem, _feeds, _testedCommits, _buildInfo );
                if( !b.Build( m ) ) return false;
                using( m.OpenInfo( $"Upgrading Build projects dependencies and removing LocalFeed NuGet sources." ) )
                {
                    foreach( var s in r.Solutions )
                    {
                        if( !s.UpdatePackageDependencies( m, _feeds.GetBestAnyLocalVersion, FileSystem, allowDowngrade: false, buildProjects: true ) ) return false;
                        if( !s.Solution.GitFolder.RemoveLocalFeedsNuGetSource( m ).Success ) return false;
                        if( !s.Solution.GitFolder.Commit( m, "Updated Build projects and removed LocalFeed NuGet sources." ).Success ) return false;
                    }
                }
                if( !SetState( m, WorkStatus.Idle, GlobalGitStatus.DevelopBranch ) ) return false;
            }
            else if( _workStatus == WorkStatus.SwitchingToLocal )
            {
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchFromDevelopToLocal( m ) ) return false;
                }
                var r = GetSolutionDependencyResult( m, World.LocalBranchName );
                if( r == null ) return false;
                _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
                var b = new GlobalBuilder( r, FileSystem, _feeds, _testedCommits, _buildInfo );
                if( !b.Build( m ) ) return false;
                if( !SetState( m, WorkStatus.Idle, GlobalGitStatus.LocalBranch ) ) return false;
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
                var b = new GlobalBuilderRelease( r, FileSystem, _feeds, _testedCommits, _buildInfo, roadmap );
                if( !b.Build( m ) ) return false;
                FileSystem.RawDeleteLocalDirectory( m, _feeds.GetReleaseFeedFolder( m ).PhysicalPath );
                using( m.OpenInfo( $"Upgrading Build projects dependencies to use CI builds." ) )
                {
                    foreach( var s in r.Solutions )
                    {
                        if( !s.UpdatePackageDependencies( m, _feeds.GetBestAnyLocalVersion, FileSystem, allowDowngrade: true, buildProjects: true ) ) return false;
                        if( !s.Solution.GitFolder.Commit( m, "Updated Build projects to use CI builds." ).Success ) return false;
                    }
                }
                using( m.OpenInfo( $"Clearing NuGet local cache." ) )
                {
                    var publishedProjects = roadmap.SelectMany( e => r.Solutions.First( s => s.Solution.UniqueSolutionName == e.SolutionName )
                                                                            .Solution
                                                                            .PublishedProjects
                                                                            .Select( p => (P: p, V: e.TargetVersion) ) );
                    foreach( var p in publishedProjects )
                    {
                        _feeds.RemoveFromNuGetCache( m, p.P.Name, p.V );
                    }
                }
                if( !SetState( m, WorkStatus.Idle, GlobalGitStatus.DevelopBranch ) ) return false;
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
            }
            return new SimpleRoadmap( roadmap );
        }

        bool DoRelease( IActivityMonitor m, SolutionDependencyResult r, SimpleRoadmap roadmap )
        {
            Debug.Assert( _workStatus == WorkStatus.Releasing );
            _buildInfo.SetStatus( WorkStatus, GlobalGitStatus );
            var b = new GlobalBuilderRelease( r, FileSystem, _feeds, _testedCommits, _buildInfo, roadmap );
            if( !b.Build( m ) ) return false;
            return SetState( m, WorkStatus.WaitingReleaseConfirmation );
        }

        public bool CanSwitchToDevelop => WorkStatus == WorkStatus.Idle && GlobalGitStatus == GlobalGitStatus.LocalBranch;

        public bool SwitchToDevelop( IActivityMonitor m )
        {
            if( !CanSwitchToDevelop ) throw new InvalidOperationException( nameof(CanSwitchToDevelop) );
            return SetState( m, WorkStatus.SwitchingToDevelop ) && ConcludeCurrentWork( m );
        }

        public bool CanSwitchToLocal => WorkStatus == WorkStatus.Idle && GlobalGitStatus == GlobalGitStatus.DevelopBranch;

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
            var r = GetSolutionDependencyResult( m, GlobalGitStatus == GlobalGitStatus.LocalBranch
                                                        ? World.LocalBranchName
                                                        : World.DevelopBranchName );
            if( r == null ) return false;
            var b = new GlobalBuilder( r, FileSystem, _feeds, _testedCommits, _buildInfo );
            return b.Build( m );
        }

        public bool CanLocalFixToZeroBuildProjects => WorkStatus == WorkStatus.Idle && GlobalGitStatus == GlobalGitStatus.LocalBranch;

        public bool LocalFixToZeroBuildProjects( IActivityMonitor m, bool commitChanges = false )
        {
            if( !CanLocalFixToZeroBuildProjects ) throw new InvalidOperationException( nameof( CanLocalFixToZeroBuildProjects ) );

            var r = GetSolutionDependencyResult( m, World.LocalBranchName );
            if( r == null ) return false;

            // Build projects should be handled independently.
            // The dependency graph of the build dependencies projects (igonring solutions) should be computed.
            // This works for the moment but is not the definitive implementation.
            var buildDeps = r.ProjectDependencies.BuildDependencies;
            var buildDepNames = r.ProjectDependencies.BuildDependencies.Select( p => p.Name ) ;

            using( m.OpenInfo( $"Removing {buildDepNames.Concatenate()} packages ZeroVersion from local NuGet cache." ) )
            {
                foreach( var n in buildDepNames )
                {
                    _feeds.RemoveFromNuGetCache( m, n, SVersion.ZeroVersion );
                }
            }
            foreach( var s in r.Solutions )
            {
                var projects = buildDeps.Where( d => d.PrimarySolution == s.Solution ).ToList();
                m.Info( $"Solution {s.Solution.UniqueSolutionName} has {s.Solution.BuildProjects.Count} build dependencies project(s)." );
                foreach( var p in projects.Concat( s.Solution.BuildProjects ) )
                {
                    foreach( var dep in p.Deps.Packages.Where( rawDep => buildDepNames.Contains( rawDep.PackageId ) ) )
                    {
                        p.SetPackageReferenceVersion( m, p.TargetFrameworks, dep.PackageId, SVersion.ZeroVersion );
                    }
                }
                if( !s.Solution.Save( m, FileSystem ) ) return false;
                if( projects.Count > 0 )
                {
                    using( m.OpenInfo( $"Generating ZeroVersion in LocalFeed/Local feed for packages: {projects.Select( p => p.Name ).Concatenate()}" ) )
                    {
                        var args = $@"pack --output ""{_feeds.GetLocalFeedFolder( m ).PhysicalPath}"" --include-symbols --configuration Debug /p:Version=""{SVersion.ZeroVersion}"" /p:AssemblyVersion=""{InformationalVersion.ZeroAssemblyVersion}"" /p:FileVersion=""{InformationalVersion.ZeroFileVersion}"" /p:InformationalVersion=""{InformationalVersion.ZeroInformationalVersion}"" ";
                        foreach( var p in projects )
                        {
                            var path = FileSystem.GetFileInfo( p.Path.RemoveLastPart() ).PhysicalPath;
                            FileSystem.RawDeleteLocalDirectory( m, Path.Combine( path, "bin" ) );
                            FileSystem.RawDeleteLocalDirectory( m, Path.Combine( path, "obj" ) );
                        }
                        // Quick and dirty fix: ordering by project references count may avoid here the need
                        // to sort projects according to their project to project dependencies which should be done...
                        foreach( var p in projects.OrderBy( p => p.Deps.Projects.Count ) )
                        {
                            var path = FileSystem.GetFileInfo( p.Path.RemoveLastPart() ).PhysicalPath;
                            if( !GlobalBuilder.Run( m, path, "dotnet", args ) ) return false;
                        }
                    }
                }
                using( m.OpenInfo( $"Updating standard CodeCakeBuilder files." ) )
                {
                    var codeCakeBuilderPath = s.Solution.SolutionFolderPath.AppendPart( "CodeCakeBuilder" );
                    foreach( var name in new[]
                    {
                        "Build.StandardCheckRepository.cs",
                        "Build.StandardCreateNuGetPackages.cs",
                        "Build.StandardPushNuGetPackages.cs",
                        "Build.StandardSolutionBuild.cs",
                        "Build.StandardUnitTests.cs"
                    } )
                    {
                        var path = codeCakeBuilderPath.AppendPart( name );
                        var f = FileSystem.GetFileInfo( path );
                        if( f.Exists )
                        {
                            var source = _referential.GetFileInfo( "InitialCodeCakeBuilder/" + name );
                            FileSystem.CopyTo( m, source, path );
                        }
                    }
                }
                if( commitChanges && !s.Solution.GitFolder.Commit( m, $"Build project now uses ZeroVersion available dependencies in LocalFeed/Local feed." ).Success )
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanRelease => WorkStatus == WorkStatus.Idle && GlobalGitStatus == GlobalGitStatus.DevelopBranch;

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
            if( !SetState( m, WorkStatus.Releasing, state => state.XmlState.Add( roadmap ) ) ) return false;
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
            //string storeApiKey = _publishKeyStore.GetCKSetupRemoteStorePushKey( m );
            //if( storeApiKey == null ) return false;
            //string myGetPushKey = _publishKeyStore.GetMyGetPushKey( m );
            //if( myGetPushKey == null ) return false;
            //string nuGetDirectory = _feeds.GetNuGetCommandLineDirectory( m );
            //if( nuGetDirectory == null )
            //{
            //    m.Error( "Unable to find required NuGet.exe." );
            //    return false;
            //}
            var roadmap = GetSimpleRoadmap( m );
            if( roadmap == null ) return false;

            // Collects all packages that must be pushed: all packages in LocalFeed/Releases and
            // all CI dependencies of build projects.
            var toPush = new HashSet<LocalPackageFile>( _feeds.GetAllPackageFilesInReleaseFeed( m ) );
            bool hasError = false;
            foreach( var p in toPush.Where( p => !p.Version.IsValid || p.Version.AsCSVersion == null ) )
            {
                m.Error( $"Invalid version {p.Version} for package {p.PackageId} in release feed. Release feed can contain only valid CSemVer versions." );
                hasError = true;
            }
            if( hasError ) return false;
            // Adds CI builds required for Build projects.
            // The list of build dependencies packages should be in the roadmap/XmlState.
            IReadOnlyList<Solution> solutions = _branchSolutionsLoader( m, World.DevelopBranchName );
            if( solutions.Any( s => s == null ) ) return false;

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

            if( WorkStatus == WorkStatus.WaitingReleaseConfirmation && !SetState( m, WorkStatus.PublishingRelease ) ) return false;

            // Push LocalFeed/Release/RemoteStore
            //string storePath = _feeds.GetReleaseCKSetupStorePath( m );
            //using( LocalStore store = LocalStore.OpenOrCreate( m, storePath ) )
            //{
            //    if( store == null || !store.PushComponents( comp => true, Facade.DefaultStoreUrl, storeApiKey ) ) return false;
            //}
            //// Push packages to myget.
            //if( !PushNuGetPackages( m, nuGetDirectory, myGetPushKey, toPush ) ) return false;

            // Push release tags, masters (for Official Release) and develops branches.
            foreach( var g in _globalGitContext.GitFolders )
            {
                var v = roadmap.FirstOrDefault( e => e.SolutionName == g.SubPath.LastPart ).TargetVersion;
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

        bool PushNuGetPackages( IActivityMonitor m, string nuGetDirectory, string myGetPushKey, IEnumerable<LocalPackageFile> files )
        {
            using( m.OpenInfo( "Pushing packages to MyGet." ) )
            {
                try
                {
                    foreach( var p in files )
                    {
                        string feedName;
                        var csVersion = p.Version.AsCSVersion;
                        if( csVersion == null ) feedName = "invenietis-ci";
                        else
                        {
                            string prerelease = csVersion.PrereleaseName;
                            feedName = prerelease.Length == 0 || prerelease == "rc" || prerelease == "pre"
                                            ? "invenietis-release"
                                            : "invenietis-preview";
                        }
                        string pushUrl = $"https://www.myget.org/F/{feedName}/api/v2/package";
                        string pushSymbolUrl = $"https://www.myget.org/F/{feedName}/symbols/api/v2/package";

                        var args = $"push \"{p.FullPath}\" -ApiKey {myGetPushKey} -Source {pushUrl} -SymbolApiKey {myGetPushKey} -SymbolSource {pushSymbolUrl}";
                        if( !GlobalBuilder.Run( m, nuGetDirectory, Path.Combine( nuGetDirectory, "NuGet.exe" ), args ) ) return false;
                    }
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return false;
                }
            }
            return true;
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
                r.RawSorterResult.LogError( m );
                return null;
            }
            return r;
        }

        public static WorldContext Create(
            IActivityMonitor m,
            IWorldName world,
            IWorldStore worldStore,
            ILocalFeedProvider feeds,
            IFileProvider referential,
            IPublishKeyStore publishKeyStore,
            IEnumerable<GitFolder> gitFolders,
            Func<IActivityMonitor, string, IReadOnlyList<Solution>> branchSolutionsLoader )
        {
            var gitContext = new GlobalGitContext( world, gitFolders );
            var worldState = worldStore.GetLocalState( m, world );
            var workStatus = worldState.XmlState.AttributeEnum( xWorkStatus, WorkStatus.Idle );
            var gitStatus = worldState.GlobalGitStatus;
            if( !gitContext.CheckStatus( m, ref gitStatus ) ) return null;
            Debug.Assert( gitStatus != GlobalGitStatus.Unknwon );
            worldState.GlobalGitStatus = gitStatus;
            return new WorldContext( gitContext, worldStore, feeds, referential, publishKeyStore, workStatus, worldState, branchSolutionsLoader );
        }

    }
}
