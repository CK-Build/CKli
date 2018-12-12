using CK.Core;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// 
    /// </summary>
    public partial class WorldState : IWorldState, ISolutionDriverWorld, ICommandMethodsProvider
    {
        readonly IWorldStore _store;
        readonly IDependentSolutionContextLoader _solutionContextLoader;
        readonly IEnvLocalFeedProvider _localFeedProvider;

        readonly HashSet<ISolutionDriver> _solutionDrivers;
        readonly Dictionary<string,ISolutionDriver> _cacheBySolutionName;
        readonly HashSet<IGitRepository> _gitRepositories;

        RawXmlWorldState _rawState;
        ReleaseRoadmap _roadMap;
        StandardGitStatus _cachedGlobalGitStatus;
        bool _isStateDirty;

        /// <summary>
        /// Initializes a new WorldState.
        /// </summary>
        /// <param name="store">The store. Can not be null.</param>
        /// <param name="solutionContextLoader">Loader of solution dependency context.</param>
        /// <param name="localFeedProvider">Local feed provider.</param>
        public WorldState(
            CommandRegister commandRegister,
            IWorldStore store,
            IWorldName worldName,
            IDependentSolutionContextLoader solutionContextLoader,
            IEnvLocalFeedProvider localFeedProvider )
        {
            if( store == null ) throw new ArgumentNullException( nameof( store ) );
            if( worldName == null ) throw new ArgumentNullException( nameof( worldName ) );
            if( solutionContextLoader == null ) throw new ArgumentNullException( nameof( solutionContextLoader ) );
            _store = store;
            WorldName = worldName;
            _solutionContextLoader = solutionContextLoader;
            _localFeedProvider = localFeedProvider;
            Debug.Assert( ((int[])Enum.GetValues( typeof( GlobalWorkStatus ) )).SequenceEqual( Enumerable.Range( 0, 8 ) ) );
            _roWorkState = new XElement[8];
            _solutionDrivers = new HashSet<ISolutionDriver>();
            _cacheBySolutionName = new Dictionary<string, ISolutionDriver>();
            _gitRepositories = new HashSet<IGitRepository>();

            CommandProviderName = "World";
            commandRegister.Register( this );
        }

        public NormalizedPath CommandProviderName { get; }

        void ISolutionDriverWorld.Register( ISolutionDriver driver )
        {
            if( !_solutionDrivers.Add( driver ) ) throw new InvalidOperationException( "Already registered." );
            if( _gitRepositories.Add( driver.GitRepository ) )
            {
                UpdateGlobalGitStatus();
            }
        }

        void ISolutionDriverWorld.Unregister(ISolutionDriver driver)
        {
            if( !_solutionDrivers.Remove( driver ) ) throw new InvalidOperationException( "Not registered." );
            foreach( var k in _cacheBySolutionName.Where( kv => kv.Value == driver ).Select( kv => kv.Key ).ToList() )
            {
                _cacheBySolutionName.Remove( k );
            }
            _gitRepositories.Clear();
            _gitRepositories.AddRange( _solutionDrivers.Select( d => d.GitRepository ) );
            UpdateGlobalGitStatus();
        }

        void RawStateChanged( object sender, XObjectChangeEventArgs e )
        {
            _isStateDirty = true;
            SetReadonlyState();
        }

        /// <summary>
        /// Gets whether this state is dirty.
        /// </summary>
        public bool IsDirty => _isStateDirty;

        /// <summary>
        /// Saves this state. If <see cref="IsDirty"/> is false, nothing is done.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Save( IActivityMonitor m )
        {
            if( _isStateDirty )
            {
                if( !_store.SetLocalState( m, _rawState ) ) return false;
                _isStateDirty = false;
                SetReadonlyState();
            }
            return true;
        }

        /// <summary>
        /// Gets the world name.
        /// </summary>
        public IWorldName WorldName { get; }

        bool IsInitialized => _rawState != null;

        /// <summary>
        /// Initializes or reinitializes this world state.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Initialize( IActivityMonitor monitor )
        {
            if( _rawState == null )
            {
                _rawState = _store.GetOrCreateLocalState( monitor, WorldName );
                SetReadonlyState();
                _rawState.Document.Changed += RawStateChanged;
            }
            return RunSafe( monitor, "Initializing World.", ( m, error ) =>
            {
                var ev = new EventMonitoredArgs( monitor );

                var called = new HashSet<object>();
                bool again;
                do
                {
                    again = false;
                    var handlers = Initializing?.GetInvocationList();
                    if( handlers != null )
                    {
                        foreach( var h in handlers )
                        {
                            if( called.Add( h ) )
                            {
                                h.DynamicInvoke( this, ev );
                                again = !error();
                            }
                        }
                    }
                }
                while( again );
                if( !error() )
                {
                    Initialized?.Invoke( this, ev );
                }
            } );
        }

        /// <summary>
        /// Gets the solution drivers.
        /// Empty until <see cref="Initialize"/> has been called.
        /// </summary>
        public IReadOnlyCollection<ISolutionDriver> SolutionDrivers => _solutionDrivers;

        /// <summary>
        /// Finds the <see cref="ISolutionDriver"/> given a branch and a solution name that can be
        /// a primary or a secondary solution.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="uniqueSolutionName">Solution name.</param>
        /// <param name="branchName">Branch name.</param>
        /// <param name="throwOnNotFound">
        /// False to return null and log an error instead of throwing an InvalidOperatioException.
        /// </param>
        /// <returns>The driver or null if not found.</returns>
        public ISolutionDriver FindSolutionDriver( IActivityMonitor monitor, string uniqueSolutionName, string branchName, bool throwOnNotFound = true )
        {
            if( String.IsNullOrWhiteSpace( uniqueSolutionName ) ) throw new ArgumentNullException( nameof( uniqueSolutionName ) );
            if( String.IsNullOrWhiteSpace( branchName)) throw new ArgumentNullException( nameof( branchName ) );
            if( !_cacheBySolutionName.TryGetValue( branchName + ':' + uniqueSolutionName, out var driver ) )
            {
                string primary = uniqueSolutionName;
                int idx = uniqueSolutionName.IndexOf( '/' );
                if( idx == 0 ) throw new ArgumentException( "Invalid solution name.", nameof( uniqueSolutionName ) );
                if( idx > 0 ) primary = uniqueSolutionName.Substring( 0, idx );
                driver = _solutionDrivers.FirstOrDefault( d => d.BranchName == branchName && d.GitRepository.PrimarySolutionName == primary );
                if( driver != null )
                {
                    Debug.Assert( driver.GetSolutionNames( monitor ).Contains( primary ) );
                    foreach( var n in driver.GetSolutionNames( monitor ) )
                    {
                        _cacheBySolutionName.Add( branchName + ':' + n, driver );
                    }
                }
                else if( throwOnNotFound )
                {
                    throw new InvalidOperationException( $"Unable to find driver for '{uniqueSolutionName}' in branch {branchName}." );
                }
            }
            return driver;
        }

        /// <summary>
        /// Finds the <see cref="ISolutionDriver"/> for a <see cref="IDependentSolution"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="solution">Solution.</param>
        /// <param name="throwOnNotFound">
        /// False to return null and log an error instead of throwing an InvalidOperatioException.
        /// </param>
        /// <returns>The driver or null if not found.</returns>
        public ISolutionDriver FindSolutionDriver( IActivityMonitor monitor, IDependentSolution solution, bool throwOnNotFound = true )
        {
            if( solution == null ) throw new ArgumentNullException( nameof( solution ) );
            return FindSolutionDriver( monitor, solution.UniqueSolutionName, solution.BranchName, throwOnNotFound );
        }

        bool RunSafe( IActivityMonitor m, string message, Action<IActivityMonitor, Func<bool>> action )
        {
            bool result = true;
            using( m.OnError( () => result = false ) )
            using( m.OpenInfo( message ) )
            {
                try
                {
                    action( m, () => !result );
                }
                catch( Exception ex )
                {
                    m.Error( $"Executing: {message}", ex is System.Reflection.TargetInvocationException t ? t.InnerException : ex );
                }
                if( !result ) m.CloseGroup( "Failed." );
                UpdateGlobalGitStatus();
                m.CloseGroup( $"{(result ? "Success" : "Failed")} => WorkStatus = {WorkStatus}, GlobalGitStatus = {_cachedGlobalGitStatus}" );
            }
            return result;
        }

        /// <summary>
        /// Computes the current git status that applies to the whole world
        /// and updates the <see cref="CachedGlobalGitStatus"/>.
        /// </summary>
        /// <returns>The standard Git status.</returns>
        StandardGitStatus UpdateGlobalGitStatus()
        {
            _cachedGlobalGitStatus = StandardGitStatus.Unknwon;
            foreach( var r in _gitRepositories )
            {
                if( r.StandardGitStatus == StandardGitStatus.Unknwon ) return _cachedGlobalGitStatus = StandardGitStatus.Unknwon;
                _cachedGlobalGitStatus |= r.StandardGitStatus;
            }
            return _cachedGlobalGitStatus;
        }

        /// <summary>
        /// Calls <see cref="UpdateGlobalGitStatus"/> and checks the curent Git status.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="expected">Expected or rejected (depends on <paramref name="not"/>).</param>
        /// <param name="not">True to consider the <paramref name="expected"/> to actually be rejected.</param>
        /// <returns>True if the expected/rejected Git status match.</returns>
        public bool CheckGlobalGitStatus( IActivityMonitor monitor, StandardGitStatus expected, bool not = false )
        {
            var s = UpdateGlobalGitStatus();
            if( not ? s == expected : s != expected )
            {
                if( not ) monitor.Error( $"Expected GlobalGitStatus to not be '{expected}'." );
                else monitor.Error( $"Expected GlobalGitStatus to be '{expected}' but it is '{s}'." );
                return false;
            }
            return true;
        }

        /// <summary>
        /// Calls <see cref="UpdateGlobalGitStatus"/> and checks the curent Git status.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True if the Git status is on 'local' or 'develop' branch.</returns>
        public bool CheckGlobalGitStatusLocalXorDevelop( IActivityMonitor monitor )
        {
            var s = UpdateGlobalGitStatus();
            if( s != StandardGitStatus.Local && s != StandardGitStatus.Develop )
            {
                monitor.Error( $"Repositories must all be on '{WorldName.LocalBranchName}' or '{WorldName.DevelopBranchName}' (current status: {s})." );
                return false;
            }
            return true;
        }

        /// <summary>
        /// Gets the global status previously computed by <see cref="GetGlobalGitStatus(IActivityMonitor)"/>.
        /// </summary>
        public StandardGitStatus CachedGlobalGitStatus => _cachedGlobalGitStatus;

        /// <summary>
        /// Gets the global work status.
        /// Null when <see cref="IsInitialized"/> is false.
        /// </summary>
        public GlobalWorkStatus? WorkStatus => _rawState?.WorkStatus;

        /// <summary>
        /// Gets the operation name (when <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.OtherOperation"/>).
        /// </summary>
        public string OtherOperationName => _rawState?.OtherOperationName;

        /// <summary>
        /// Gets the mutable <see cref="XElement"/> general state.
        /// This is where state information that are not specific to an operation are stored.
        /// </summary>
        public XElement GeneralState => _rawState?.GeneralState;

        /// <summary>
        /// Gets the mutable <see cref="XElement"/> state for an operation.
        /// </summary>
        /// <param name="status">The work status.</param>
        /// <returns>The state element.</returns>
        public XElement GetWorkState( GlobalWorkStatus status ) => _rawState?.GetWorkState( status );

        /// <summary>
        /// Sets the <see cref="WorkStatus"/>.
        /// </summary>
        /// <param name="s">The work status.</param>
        /// <param name="otherOperationName">Operation name for <see cref="GlobalWorkStatus.OtherOperation"/>.</param>
        public void SetWorkStatus( GlobalWorkStatus s, string otherOperationName = null )
        {
            if( s == GlobalWorkStatus.OtherOperation == String.IsNullOrWhiteSpace( otherOperationName ) )
            {
                throw new ArgumentException( $"Incompatible operation name.", nameof( otherOperationName ) );
            }
            if( WorkStatus != s || OtherOperationName != otherOperationName )
            {
                _rawState.WorkStatus = s;
                _rawState.OtherOperationName = otherOperationName;
            }
        }

        bool SetWorkStatusAndSave( IActivityMonitor m, GlobalWorkStatus s, string otherOperationName = null )
        {
            SetWorkStatus( s, otherOperationName );
            return Save( m );
        }

        /// <summary>
        /// Gets whether the <see cref="WorkStatus"/> is not <see cref="GlobalWorkStatus.Idle"/>
        /// nor <see cref="GlobalWorkStatus.WaitingReleaseConfirmation"/>.
        /// </summary>
        public bool IsConcludeCurrentWorkEnabled => IsInitialized
                                                    && WorkStatus != GlobalWorkStatus.Idle
                                                    && WorkStatus != GlobalWorkStatus.WaitingReleaseConfirmation;

        /// <summary>
        /// Gets the <see cref="IDependentSolutionContext"/> after having called <see cref="UpdateGlobalGitStatus"/>
        /// and checked that repositories are all on 'local' or 'develop' branch.
        /// <see cref="CachedGlobalGitStatus"/> is up-to-date after this call.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="reloadSolutions">True to force a reload of the solutions.</param>
        /// <returns>The dependency context or null on error.</returns>
        IDependentSolutionContext GetSolutionDependentContext( IActivityMonitor monitor, bool checkCleanCommits, bool reloadSolutions = false )
        {
            if( !CheckGlobalGitStatusLocalXorDevelop( monitor ) ) return null;
            if( checkCleanCommits )
            {
                bool allClean = true;
                foreach( var g in _gitRepositories )
                {
                    allClean &= g.CheckCleanCommit( monitor ); 
                }
                if( !allClean ) return null;
            }
            var branchName = CachedGlobalGitStatus == StandardGitStatus.Local
                                ? WorldName.LocalBranchName
                                : WorldName.DevelopBranchName;
            return _solutionContextLoader.Load( monitor, _gitRepositories, branchName, reloadSolutions );
        }

        /// <summary>
        /// Concludes the current work.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool ConcludeCurrentWork( IActivityMonitor m )
        {
            if( !IsConcludeCurrentWorkEnabled ) throw new InvalidOperationException( nameof( IsConcludeCurrentWorkEnabled ) );
            switch( WorkStatus )
            {
                case GlobalWorkStatus.SwitchingToLocal: if( !DoSwitchToLocal( m ) ) return false; break;
                case GlobalWorkStatus.SwitchingToDevelop: if( !DoSwitchToDevelop( m ) ) return false; break;
                case GlobalWorkStatus.Releasing: if( !DoReleasing( m ) ) return false; break;
                case GlobalWorkStatus.CancellingRelease: if( !DoCancellingRelease( m ) ) return false; break;
                case GlobalWorkStatus.PublishingRelease: if( !DoPublishingRelease( m ) ) return false; break;
                case GlobalWorkStatus.OtherOperation: if( !DoOtherOperation( m ) ) return false; break;
                default: Debug.Fail( "Unreachable code." ); break;
            }
            return true;
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/> and <see cref="CachedGlobalGitStatus"/>
        /// is <see cref="StandardGitStatus.Develop"/> or <see cref="StandardGitStatus.DevelopOrLocal"/>.
        /// </summary>
        public bool CanSwitchToLocal => IsInitialized
                                        && WorkStatus == GlobalWorkStatus.Idle
                                        && (CachedGlobalGitStatus == StandardGitStatus.Develop
                                            || CachedGlobalGitStatus == StandardGitStatus.DevelopOrLocal);

        /// <summary>
        /// Switches from develop to local branch.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool SwitchToLocal( IActivityMonitor monitor )
        {
            if( !CanSwitchToLocal ) throw new InvalidOperationException( nameof( CanSwitchToLocal ) );
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.Unknwon, not: true ) ) return false;
            if( !SetWorkStatusAndSave( monitor, GlobalWorkStatus.SwitchingToLocal ) ) return false;
            return DoSwitchToLocal( monitor );
        }

        bool DoSwitchToLocal( IActivityMonitor monitor )
        {
            Debug.Assert( WorkStatus == GlobalWorkStatus.SwitchingToLocal );
            return RunSafe( monitor, $"Switching to {WorldName.LocalBranchName}.", (m,error) =>
            {
                var ev = new EventMonitoredArgs( m );
                SwitchingToLocal?.Invoke( this, ev );
                if( error() ) return;

                foreach( var g in _gitRepositories )
                {
                    if( !g.SwitchDevelopToLocal( m, autoCommit: true ) ) return;
                }

                var depContext = GetSolutionDependentContext( m, true );
                if( depContext == null ) return;
                if( !EnsureZeroBuildProjects( m, depContext ) ) return;
                if( !error() )
                {
                    SwitchedToLocal?.Invoke( this, ev );
                    SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
                }
            } );
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/> and <see cref="CachedGlobalGitStatus"/>
        /// is on <see cref="StandardGitStatus.Local"/> or <see cref="StandardGitStatus.Develop"/>.
        /// </summary>
        public bool CanZeroBuildProjects => IsInitialized
                                            && WorkStatus == GlobalWorkStatus.Idle
                                            && (CachedGlobalGitStatus == StandardGitStatus.Local
                                                || CachedGlobalGitStatus == StandardGitStatus.Develop);

        /// <summary>
        /// Fix dependency issues among build projects.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool ZeroBuildProjects( IActivityMonitor monitor )
        {
            if( !CanZeroBuildProjects ) throw new InvalidOperationException( nameof( CanZeroBuildProjects ) );
            if( !CheckGlobalGitStatusLocalXorDevelop( monitor ) ) return false;
            return RunSafe( monitor, "Fixing Build projects.", ( m, error ) =>
            {
                var depContext = GetSolutionDependentContext( m, true );
                if( depContext == null ) return;
                EnsureZeroBuildProjects( m, depContext );
            } );
        }

        bool EnsureZeroBuildProjects( IActivityMonitor m, IDependentSolutionContext depContext )
        {
            Debug.Assert( CachedGlobalGitStatus == StandardGitStatus.Local || CachedGlobalGitStatus == StandardGitStatus.Develop );
            Debug.Assert( depContext.UniqueBranchName == WorldName.DevelopBranchName || depContext.UniqueBranchName == WorldName.LocalBranchName );
            if( depContext.BuildProjectsInfo == null )
            {
                m.Error( "Build Projects dependencies failed to be computed." );
                return false;
            }
            if( depContext.BuildProjectsInfo.Count == 0 )
            {
                m.Info( "No Build Project exist." );
                return true;
            }

            using( m.OpenInfo( $"Building ZeroVersion projects." ) )
            {
                var mustBuild = new HashSet<string>();
                mustBuild.AddRange( depContext.BuildProjectsInfo.Select( z => z.FullName ) );

                var memPath = _localFeedProvider.ZeroBuild.PhysicalPath.AppendPart( "CacheZeroVersion.txt" );
                var sha1Cache = System.IO.File.Exists( memPath )
                                ? System.IO.File.ReadAllLines( memPath )
                                                .Select( l => l.Split() )
                                                .Where( l => mustBuild.Contains( l[0] ) )
                                                .ToDictionary( l => l[0], l => l[1] )
                                : new Dictionary<string, string>();
                m.Info( $"File '{memPath}' contains {sha1Cache.Count} entries." );

                var currentShas = new string[depContext.BuildProjectsInfo.Count];
                var driverMap = new Dictionary<string, ISolutionDriver>();
                var scopeMap = new Dictionary<ISolutionDriver, IDisposable>();
                try
                {
                    using( m.OpenTrace( "Resolving drivers and reading Sha signatures." ) )
                    {
                        foreach( var p in depContext.BuildProjectsInfo )
                        {
                            if( !driverMap.TryGetValue( p.SolutionName, out var d ) )
                            {
                                d = FindSolutionDriver( m, p.SolutionName, depContext.UniqueBranchName );
                                driverMap.Add( p.SolutionName, d );
                            }
                            currentShas[p.Index] = d.GitRepository.Head.GetSha( p.PrimarySolutionRelativeFolderPath );
                        }
                    }
                    using( m.OpenTrace( "Analysing dependencies." ) )
                    {
                        foreach( var p in depContext.BuildProjectsInfo )
                        {
                            using( m.OpenInfo( $"{p} <= {(p.Dependencies.Any() ? p.Dependencies.Concatenate() : "(no dependency)")}." ) )
                            {
                                var driver = driverMap[p.SolutionName];

                                // Check cache.
                                var currentTreeSha = currentShas[p.Index];
                                if( currentTreeSha == null )
                                {
                                    throw new Exception( $"Unable to get Sha for {p.PrimarySolutionRelativeFolderPath}." );
                                }
                                if( !sha1Cache.TryGetValue( p.FullName, out var sha ) )
                                {
                                    m.Info( $"ReasonToBuild#1: No cached Sha signature found for {p.FullName}." );
                                }
                                else if( sha != currentTreeSha )
                                {
                                    m.Info( $"ReasonToBuild#2: Current Sha signature differs from the cached one." );
                                }
                                else if( p.Dependencies.Any( depName => mustBuild.Contains( depName ) ) )
                                {
                                    m.Info( $"ReasonToBuild#3: Rebuild dependencies {mustBuild.Intersect( p.Dependencies ).Concatenate()}." );
                                }
                                else if( p.MustPack
                                         && _localFeedProvider.ZeroBuild.GetPackageFile( m, p.ProjectName, SVersion.ZeroVersion ) == null )
                                {
                                    m.Info( $"ReasonToBuild#4: {p.ProjectName}.0.0.0-0 does not exist in in Zero build feed." );
                                }
                                else
                                {
                                    mustBuild.Remove( p.FullName );
                                    m.CloseGroup( $"Project '{p}' is up to date. Build skipped." );
                                    continue;
                                }
                            }
                        }
                    }
                    if( mustBuild.Count == 0 ) m.Info( "Nothing to build. Build projects are up-to-date." );
                    else
                    {
                        using( m.OpenTrace( "Creating protected scopes and applying zero dependencies." ) )
                        {
                            foreach( var p in depContext.BuildProjectsInfo )
                            {
                                using( m.OpenInfo( $"Configuring {p}." ) )
                                {
                                    var driver = driverMap[p.SolutionName];
                                    if( !scopeMap.ContainsKey( driver ) )
                                    {
                                        scopeMap.Add( driver, driver.GitRepository.OpenProtectedScope( m, null ) );
                                    }
                                    // Always sets Zero version dependencies even if we don't build it so that
                                    // dependent project see homogeneous Zero versions for all its dependencies.
                                    var zeroDeps = p.UpgradePackages.Select( dep => new UpdatePackageInfo( p.SolutionName, p.ProjectName, dep, SVersion.ZeroVersion ) );
                                    if( !driver.UpdatePackageDependencies( m, zeroDeps ) ) return false;
                                }
                            }
                        }

                        using( m.OpenTrace( $"Build/Publish {mustBuild.Count} build projects: {mustBuild.Concatenate()}" ) )
                        {
                            foreach( var p in depContext.BuildProjectsInfo.Where( p => mustBuild.Contains( p.FullName ) ) )
                            {
                                var action = p.MustPack ? "Publishing" : "Building";
                                using( m.OpenInfo( $"{action} {p}." ) )
                                {
                                    var driver = driverMap[p.SolutionName];
                                    if( !driver.ZeroBuildProject( m, p ) )
                                    {
                                        sha1Cache.Remove( p.FullName );
                                        m.CloseGroup( "Failed." );
                                        return false;
                                    }
                                    sha1Cache[p.FullName] = currentShas[p.Index];
                                    m.CloseGroup( "Success." );
                                }
                            }
                        }
                    }
                    return true;
                }
                finally
                {
                    foreach( var scope in scopeMap.Values ) scope.Dispose();
                    m.Info( $"Saving {sha1Cache.Count} entries in file '{memPath}'." );
                    System.IO.File.WriteAllLines( memPath, sha1Cache.Select( kv => kv.Key + ' ' + kv.Value ) );
                }
            }
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/> and <see cref="CachedGlobalGitStatus"/>
        /// is on <see cref="StandardGitStatus.Local"/> or <see cref="StandardGitStatus.Develop"/>.
        /// </summary>
        public bool CanAllBuild => WorkStatus == GlobalWorkStatus.Idle
                                   && (CachedGlobalGitStatus == StandardGitStatus.Local
                                       || CachedGlobalGitStatus == StandardGitStatus.Develop);

        [CommandMethod]
        public bool AllBuild( IActivityMonitor monitor, bool withUnitTest = true )
        {
            if( !CanAllBuild ) throw new InvalidOperationException( nameof( CanAllBuild ) );
            if( !CheckGlobalGitStatusLocalXorDevelop( monitor ) ) return false;
            return RunSafe( monitor, $"Local build.", ( m, error ) =>
            {
                var depContext = GetSolutionDependentContext( m, true );
                if( depContext == null ) return;

                if( !EnsureZeroBuildProjects( m, depContext ) ) return;

                Builder builder = CachedGlobalGitStatus == StandardGitStatus.Local
                                ? (Builder)new LocalBuilder( depContext, ( mon, s ) => FindSolutionDriver( mon, s, true ), withUnitTest )
                                : new DevelopBuilder( depContext, ( mon, s ) => FindSolutionDriver( mon, s, true ), withUnitTest );
                builder.Run( m );
            } );
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/> and <see cref="CachedGlobalGitStatus"/>
        /// is on <see cref="StandardGitStatus.Local"/> or on a mix of 'develop' and 'local' (<see cref="StandardGitStatus.DevelopOrLocal"/>).
        /// </summary>
        public bool CanSwitchToDevelop => IsInitialized
                                          && WorkStatus == GlobalWorkStatus.Idle
                                          && (CachedGlobalGitStatus & StandardGitStatus.Local) != 0
                                          && (CachedGlobalGitStatus & StandardGitStatus.Master) == 0;

        /// <summary>
        /// Switches back from local to develop branch.
        /// Must throw an <see cref="InvalidOperationException"/> if <see cref="CanSwitchToDevelop"/> is false.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool SwitchToDevelop( IActivityMonitor monitor )
        {
            if( !CanSwitchToDevelop ) throw new InvalidOperationException( nameof( CanSwitchToDevelop ) );
            var s = UpdateGlobalGitStatus();
            if( (s & StandardGitStatus.Local) == 0 || (s & StandardGitStatus.Master) != 0 )
            {
                monitor.Error( $"At least one repository must be on '{WorldName.LocalBranchName}', some may be on '{WorldName.DevelopBranchName}'." );
                return false;
            }
            if( !SetWorkStatusAndSave( monitor, GlobalWorkStatus.SwitchingToDevelop ) ) return false;
            return DoSwitchToDevelop( monitor );
        }

        bool DoSwitchToDevelop( IActivityMonitor monitor )
        {
            Debug.Assert( WorkStatus == GlobalWorkStatus.SwitchingToDevelop );
            return RunSafe( monitor, $"Switching to {WorldName.DevelopBranchName}.", ( m, error ) =>
            {
                var ev = new EventMonitoredArgs( m );
                SwitchingToDevelop?.Invoke( this, ev );
                if( error() ) return;

                foreach( var g in _gitRepositories )
                {
                    if( !g.SwitchLocalToDevelop( m ) ) return;
                }
                var depContext = GetSolutionDependentContext( m, true );
                if( depContext == null ) return;

                if( !EnsureZeroBuildProjects( m, depContext ) ) return;

                var builder = new DevelopBuilder( depContext, ( mon, s ) => FindSolutionDriver( mon, s ), false );
                if( !builder.Run( m ) ) return;

                if( !error() )
                {
                    SwitchedToDevelop?.Invoke( this, ev );
                    SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
                }
            } );
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/> and <see cref="CachedGlobalGitStatus"/>
        /// is on <see cref="StandardGitStatus.Develop"/>.
        /// </summary>
        public bool CanRelease => IsInitialized
                                  && WorkStatus == GlobalWorkStatus.Idle
                                  && CachedGlobalGitStatus == StandardGitStatus.Develop;

        /// <summary>
        /// Gets or creates the current roadmap that can be modified.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The roadmap.</returns>
        public ReleaseRoadmap EnsureRoadmap( IActivityMonitor monitor, bool reload = false )
        {
            if( !CanRelease ) throw new InvalidOperationException( nameof( CanRelease ) );
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.Develop ) ) return null;
            return DoEnsureRoadmap( monitor, reload );
        }

        ReleaseRoadmap DoEnsureRoadmap( IActivityMonitor monitor, bool reload )
        {
            if( _roadMap != null && !reload ) return _roadMap;
            var depContext = GetSolutionDependentContext( monitor, true, reload );
            if( depContext == null ) return null;
            var previous = _roadMap != null ? _roadMap.ToXml() : GetWorkState( GlobalWorkStatus.Releasing ).Element( "RoadMap" );
            return _roadMap = ReleaseRoadmap.Create( monitor, depContext, previous );
        }

        /// <summary>
        /// Starts a release after an optional pull.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="pull">Pull all branches first.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Release( IActivityMonitor monitor, IReleaseVersionSelector versionSelector, bool pull = true )
        {
            if( !CanRelease ) throw new InvalidOperationException( nameof( CanRelease ) );
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.Develop ) ) return false;

            bool reloadNeeded = false;
            foreach( var g in _gitRepositories )
            {
                if( !g.CheckCleanCommit( monitor ) ) return false;
                if( pull )
                {
                    var result = g.CheckoutAndPull( monitor, WorldName.DevelopBranchName, alwaysPullAllBranches: true );
                    if( !result.Success ) return false;
                    reloadNeeded |= result.ReloadNeeded;
                }
            }

            var roadmap = DoEnsureRoadmap( monitor, reloadNeeded );
            if( roadmap == null || !roadmap.UpdateRoadMap( monitor, versionSelector ) ) return false;

            SetWorkStatus( GlobalWorkStatus.Releasing );
            GetWorkState( GlobalWorkStatus.Releasing ).SetElementValue( "RoadMap", roadmap.ToXml() );
            if( !Save( monitor ) ) return false;
            return DoReleasing( monitor, roadmap );
        }

        bool DoReleasing( IActivityMonitor monitor, ReleaseRoadmap roadmap = null )
        {
            if( roadmap == null )
            {
                roadmap = DoEnsureRoadmap( monitor, false );
                if( roadmap == null || !roadmap.IsValid )
                {
                    monitor.Error( $"Road map is invalid. Current release should be cancelled." );
                    if( roadmap != null ) GetWorkState( GlobalWorkStatus.Releasing ).SetElementValue( "RoadMap", roadmap.ToXml() );
                    Save( monitor );
                    return false;
                }
            }
            return RunSafe( monitor, $"Starting Release.", ( m, error ) =>
            {
                var ev = new EventMonitoredArgs( m );
                ReleaseBuildStarting?.Invoke( this, ev );

                if( !error() ) ReleaseBuildDone?.Invoke( this, ev );
                SetWorkStatusAndSave( m, GlobalWorkStatus.WaitingReleaseConfirmation );
            } );

        }

        bool DoCancellingRelease( IActivityMonitor m )
        {
            throw new NotImplementedException();
        }

        bool DoPublishingRelease( IActivityMonitor m )
        {
            throw new NotImplementedException();
        }

        bool DoOtherOperation( IActivityMonitor m )
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets whether <see cref="CancelRelease"/> can be called.
        /// </summary>
        public bool CanCancelRelease => IsInitialized
                                        && (WorkStatus == GlobalWorkStatus.Releasing || WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation);


        /// <summary>
        /// Cancel the current release.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool CancelRelease( IActivityMonitor m )
        {
            if( !CanCancelRelease ) throw new InvalidOperationException( nameof( CanCancelRelease ) );
            return SetWorkStatusAndSave( m, GlobalWorkStatus.CancellingRelease ) && ConcludeCurrentWork( m );
        }

        /// <summary>
        /// Release can be published when <see cref="GlobalWorkStatus.WaitingReleaseConfirmation"/>.
        /// </summary>
        public bool CanPublishRelease => IsInitialized && WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation;

        public bool PublishRelease( IActivityMonitor m )
        {
            if( !CanPublishRelease ) throw new InvalidOperationException( nameof( CanPublishRelease ) );
            return DoPublishingRelease( m );
        }

        #region Read only State

        StandardGitStatus _roGlobalGitStatus;
        GlobalWorkStatus _roGlobalWorkStatus;
        string _roOtherOperationName;
        XElement _roGeneralState;
        readonly XElement[] _roWorkState;

        void SetReadonlyState()
        {
            _roGlobalGitStatus = CachedGlobalGitStatus;
            _roGlobalWorkStatus = _rawState.WorkStatus;
            _roOtherOperationName = _rawState.OtherOperationName;
            _roGeneralState = new XElement( _rawState.GeneralState );
            _roGeneralState.Changing += PreventChanges;
            for( int i = 0; i < _roWorkState.Length; i++ )
            {
                _roWorkState[i] = new XElement( _rawState.GetWorkState( (GlobalWorkStatus)i ) );
                _roWorkState[i].Changing += PreventChanges;
            }
        }

        static void PreventChanges( object sender, XObjectChangeEventArgs e )
        {
            throw new InvalidOperationException( "XElement is read-only." );
        }

        StandardGitStatus IWorldState.GlobalGitStatus => _roGlobalGitStatus;

        GlobalWorkStatus IWorldState.WorkStatus => _roGlobalWorkStatus;

        string IWorldState.OtherOperationName => _roOtherOperationName;

        XElement IWorldState.GeneralState => _roGeneralState;

        XElement IWorldState.GetWorkState( GlobalWorkStatus status ) => _roWorkState[(int)status];

        #endregion

    }

}
