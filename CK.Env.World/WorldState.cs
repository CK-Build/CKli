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
        readonly ArtifactCenter _artifacts;

        readonly HashSet<ISolutionDriver> _solutionDrivers;
        readonly Dictionary<string,ISolutionDriver> _cacheBySolutionName;
        readonly HashSet<IGitRepository> _gitRepositories;

        RawXmlWorldState _rawState;
        StandardGitStatus _cachedGlobalGitStatus;
        bool _isStateDirty;

        /// <summary>
        /// Initializes a new WorldState.
        /// </summary>
        /// <param name="commandRegister">The command register.</param>
        /// <param name="artifacts">The artifact center.</param>
        /// <param name="worldName">The world name.</param>
        /// <param name="store">The store. Can not be null.</param>
        /// <param name="solutionContextLoader">Loader of solution dependency context.</param>
        /// <param name="localFeedProvider">Local feed provider. Can not be null. (Required for the Zro builder.)</param>
        /// <param name="publisher">Artifacts publisher.</param>
        public WorldState(
            CommandRegister commandRegister,
            ArtifactCenter artifacts,
            IWorldStore store,
            IWorldName worldName,
            IDependentSolutionContextLoader solutionContextLoader,
            IEnvLocalFeedProvider localFeedProvider )
        {
            if( store == null ) throw new ArgumentNullException( nameof( store ) );
            if( worldName == null ) throw new ArgumentNullException( nameof( worldName ) );
            if( solutionContextLoader == null ) throw new ArgumentNullException( nameof( solutionContextLoader ) );
            if( artifacts == null ) throw new ArgumentNullException( nameof( artifacts ) );
            if( localFeedProvider == null ) throw new ArgumentNullException( nameof( localFeedProvider ) );
            _artifacts = artifacts;
            _store = store;
            WorldName = worldName;
            _solutionContextLoader = solutionContextLoader;
            _localFeedProvider = localFeedProvider;
            Debug.Assert( ((int[])Enum.GetValues( typeof( GlobalWorkStatus ) )).SequenceEqual( Enumerable.Range( 0, 7 ) ) );
            _roWorkState = new XElement[7];
            _solutionDrivers = new HashSet<ISolutionDriver>();
            _cacheBySolutionName = new Dictionary<string, ISolutionDriver>();
            _gitRepositories = new HashSet<IGitRepository>();

            CommandProviderName = "World";
            commandRegister.Register( this );
        }

        public NormalizedPath CommandProviderName { get; }

        bool IsInitialized => _rawState != null;

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

        /// <summary>
        /// Helper for external components.
        /// This throws an exception if the driver is not found.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="depContext">The current dependency context.</param>
        /// <param name="solutionName">Solution name.</param>
        /// <returns>The driver.</returns>
        public ISolutionDriver DriverFinder( IActivityMonitor monitor, IDependentSolutionContext depContext, string solutionName )
        {
            return FindSolutionDriver( monitor, solutionName, depContext.UniqueBranchName, true );
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
        bool CheckGlobalGitStatusLocalXorDevelop( IActivityMonitor monitor )
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
        public void SetWorkStatus( GlobalWorkStatus s )
        {
            if( WorkStatus != s ) _rawState.WorkStatus = s;
        }

        bool SetWorkStatusAndSave( IActivityMonitor m, GlobalWorkStatus s )
        {
            SetWorkStatus( s );
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
                UpdateGlobalGitStatus();
                Debug.Assert( CachedGlobalGitStatus == StandardGitStatus.Local );

                var depContext = GetSolutionDependentContext( m, true );
                if( depContext == null ) return;

                if( ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext, DriverFinder ) == null ) return;

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
                ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext, DriverFinder );
            } );
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

                ZeroBuilder zBuilder = ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext, DriverFinder );
                if( zBuilder == null ) return;

                Builder builder = CachedGlobalGitStatus == StandardGitStatus.Local
                                ? (Builder)new LocalBuilder( _artifacts, depContext, DriverFinder, withUnitTest )
                                : new DevelopBuilder( _artifacts, depContext, DriverFinder, withUnitTest );
                if( !RunBuild( m, builder ) ) return;

                zBuilder.RegisterSHAlias( m );
            } );
        }

        bool RunBuild( IActivityMonitor m, Builder b )
        {
            var result = b.Run( m );
            if( result == null ) return false;
            using( m.OpenInfo( $"Updating LastBuild with { result.GeneratedArtifacts.Count} artifacts." ) )
            {
                foreach( var a in result.GeneratedArtifacts.GroupBy( a => a.Artifact.Artifact.Type ) )
                {
                    m.Info( $"{a.Key} => {a.Select( p => p.Artifact.Artifact.Name + '/' + p.Artifact.Version + " -> " + p.TargetName ).Concatenate()}" );
                }
            }
            GeneralState.ReplaceElementByName( new XElement( "LastBuild", result.ToXml() ) );
            return Save( m );
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

                var zBuilder = ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext, DriverFinder );
                if( zBuilder == null ) return;

                var builder = new DevelopBuilder( _artifacts, depContext, DriverFinder, false );
                if( !RunBuild( m, builder ) ) return;

                zBuilder.RegisterSHAlias( m );

                if( !error() )
                {
                    SwitchedToDevelop?.Invoke( this, ev );
                    SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
                }
            } );
        }

        ReleaseRoadmap LoadRoadmap( IActivityMonitor monitor )
        {
            var depContext = GetSolutionDependentContext( monitor, true );
            if( depContext == null ) return null;
            var previous = GetWorkState( GlobalWorkStatus.Releasing ).Element( "Roadmap" );
            return ReleaseRoadmap.Create( monitor, depContext, previous );
        }

        /// <summary>
        /// Gets or sets the version selector that <see cref="Release"/> will use.
        /// </summary>
        public IReleaseVersionSelector VersionSelector { get; set; }

        /// <summary>
        /// Same as <see cref="CanRelease"/>.
        /// </summary>
        public bool CanEditRoadmap => CanRelease;

        /// <summary>
        /// Edits the current roadmap or creates one.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The roadmap.</returns>
        [CommandMethod]
        public bool EditRoadmap( IActivityMonitor monitor )
        {
            if( !CanEditRoadmap ) throw new InvalidOperationException( nameof( CanEditRoadmap ) );
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.Develop ) ) return false;
            return DoEditRoadmap( monitor, false ) != null;
        }

        ReleaseRoadmap DoEditRoadmap( IActivityMonitor monitor, bool skipPreviouslyResolved )
        {
            var roadmap = LoadRoadmap( monitor );
            if( roadmap == null ) return null;
            bool editSucceed = roadmap.UpdateRoadmap( monitor, VersionSelector, skipPreviouslyResolved );
            // Always saves state to preserve any change in the roadmap, even on error.
            GetWorkState( GlobalWorkStatus.Releasing ).ReplaceElementByName( roadmap.ToXml() );
            Save( monitor );
            if( !Save( monitor ) || !editSucceed ) return null;
            return roadmap;
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/>, <see cref="VersionSelector"/>
        /// and <see cref="CachedGlobalGitStatus"/> is on <see cref="StandardGitStatus.Develop"/>.
        /// </summary>
        public bool CanRelease => IsInitialized
                                  && VersionSelector != null
                                  && WorkStatus == GlobalWorkStatus.Idle
                                  && CachedGlobalGitStatus == StandardGitStatus.Develop;

        /// <summary>
        /// Starts a release after an optional pull, using the current <see cref="VersionSelector"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="checkRoadmap">
        /// True to silently skip any already set versions.
        /// When false (the default), if the road map is valid and <see cref="pull"/> did not change anything, the
        /// release is started immediately.
        /// </param>
        /// <param name="pull">Pull all branches first.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Release( IActivityMonitor monitor, bool checkRoadmap = false, bool pull = true )
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

            var roadmap = DoEditRoadmap( monitor, !checkRoadmap && !reloadNeeded );
            if( roadmap == null ) return false;

            SetWorkStatus( GlobalWorkStatus.Releasing );
            if( !Save( monitor ) ) return false;
            return DoReleasing( monitor, roadmap );
        }

        bool DoReleasing( IActivityMonitor monitor, ReleaseRoadmap roadmap = null )
        {
            if( roadmap == null )
            {
                roadmap = LoadRoadmap( monitor );
                if( roadmap == null || !roadmap.IsValid )
                {
                    monitor.Error( $"Road map is invalid. Current release should be cancelled." );
                    return false;
                }
            }
            return RunSafe( monitor, $"Starting Release.", ( m, error ) =>
            {
                var ev = new EventMonitoredArgs( m );
                ReleaseBuildStarting?.Invoke( this, ev );

                var b = new ReleaseBuilder( _artifacts, roadmap, _localFeedProvider, DriverFinder );
                if( !RunBuild( m, b ) ) return;
                if( !error() )
                {
                    ReleaseBuildDone?.Invoke( this, ev );
                    SetWorkStatusAndSave( m, GlobalWorkStatus.WaitingReleaseConfirmation );
                }
            } );

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

        bool DoCancellingRelease( IActivityMonitor m )
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Release can be published when <see cref="GlobalWorkStatus.WaitingReleaseConfirmation"/>.
        /// </summary>
        public bool CanPublishRelease => IsInitialized && WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation;

        [CommandMethod]
        public bool PublishRelease( IActivityMonitor m )
        {
            if( !CanPublishRelease ) throw new InvalidOperationException( nameof( CanPublishRelease ) );
            return DoPublishingRelease( m );
        }

        bool DoPublishingRelease( IActivityMonitor monitor )
        {
            return RunSafe( monitor, $"Publishing Release.", ( m, error ) =>
            {
                var buildResults = new BuildResult( GeneralState.Element( "LastBuild" )?.Element( "BuildResult" ) );
                if( buildResults.BuildResultType != BuildResultType.Release )
                {
                    m.Error( "LastBuild is not a release." );
                    return;
                }
                if( !DoPublish( m, buildResults, _localFeedProvider.Release ) ) return;
                if( !error() )
                {
                    SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
                }
            } );
        }

        bool DoPublish( IActivityMonitor monitor, BuildResult buildResults, IEnvLocalFeed local )
        {
            return RunSafe( monitor, $"Publishing Artifacts.", ( m, error ) =>
            {
                foreach( var a in buildResults.GeneratedArtifacts.GroupBy( a => a.TargetName ) )
                {
                    var h = _artifacts.Find( a.Key );
                    if( !local.PushLocalArtifacts( m, h, a.Select( p => p.Artifact ) ) ) return;
                }
                if( !error() )
                {
                    SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
                }
            } );
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
