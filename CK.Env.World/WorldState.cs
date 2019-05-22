using CK.Core;
using CK.Env.DependencyModel;
using CK.Text;
using CSemVer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// 
    /// </summary>
    public partial class WorldState : IWorldState, ISolutionDriverWorld, ICommandMethodsProvider
    {
        class WorldBranchContext : IWorldSolutionContext
        {
            readonly List<ISolutionDriver> _drivers;
            readonly SolutionContext _context;
            readonly PairList _pairList;
            SolutionDependencyContext _depContext;

            internal WorldBranchContext( string branchName )
            {
                BranchName = branchName;
                _context = new SolutionContext();
                _drivers = new List<ISolutionDriver>();
                _pairList = new PairList( this );
            }

            /// <summary>
            /// Gets the branch name.
            /// </summary>
            public string BranchName { get; }

            public SolutionDependencyContext DependencyContext => _depContext;

            public IReadOnlyList<DependentSolution> DependentSolutions => _depContext.Solutions;

            public IReadOnlyList<ISolutionDriver> Drivers => _drivers;

            class PairList : IReadOnlyList<(DependentSolution Solution, ISolutionDriver Driver)>
            {
                readonly WorldBranchContext _c;

                public PairList( WorldBranchContext c ) => _c = c;               

                public (DependentSolution Solution, ISolutionDriver Driver) this[int index] => (_c.DependentSolutions[index], _c._drivers[index]);

                public int Count => _c._drivers.Count;

                public IEnumerator<(DependentSolution Solution, ISolutionDriver Driver)> GetEnumerator()
                {
                    return _c.DependentSolutions.Select( s => (s, _c._drivers[s.Index]) ).GetEnumerator();
                }

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public IReadOnlyList<(DependentSolution Solution, ISolutionDriver Driver)> Solutions => _pairList;

            public bool Refresh( IActivityMonitor m, bool forceReload )
            {
                foreach( var d in _drivers )
                {
                    if( d.GetSolution( m, forceReload ) == null )
                    {
                        m.Error( $"Failed to load solution from '{d.GitRepository.SubPath}'." );
                        return false;
                    }
                }
                if( _depContext == null || _depContext.HasError || _depContext.IsObsolete )
                {
                    LogFilter final = m.ActualFilter;
                    if( final == LogFilter.Undefined ) final = ActivityMonitor.DefaultFilter;
                    _depContext = _context.GetDependencyAnalyser( m, final == LogFilter.Debug ).DefaultDependencyContext;
                    if( !_depContext.HasError )
                    {
                        Debug.Assert( _drivers.Count == _depContext.Solutions.Count );
                        _drivers.Sort( ( d1, d2 ) => _depContext[d1.GetSolution( m, false )].Index - _depContext[d2.GetSolution( m, false )].Index );
                    }
                }
                return true;
            }

            internal SolutionContext OnRegisterDriver( ISolutionDriver d )
            {
                Debug.Assert( d != null && !_drivers.Contains( d ) );
                _drivers.Add( d );
                _depContext = null;
                return _context;
            }

            internal void OnUnregisterDriver( ISolutionDriver d )
            {
                _drivers.Remove( d );
                _depContext = null;
            }
        }

        readonly IWorldStore _store;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly ArtifactCenter _artifacts;

        readonly HashSet<ISolutionDriver> _solutionDrivers;
        readonly Dictionary<string, WorldBranchContext> _perBranchContext;
        readonly HashSet<IGitRepository> _gitRepositories;
        readonly Dictionary<string, ISolutionDriver> _cacheBySolutionName;
        readonly IActivityMonitorFilteredClient _userMonitorClient;

        readonly IBasicApplicationLifetime _appLife;

        RawXmlWorldState _rawState;
        StandardGitStatus _cachedGlobalGitStatus;
        bool _isStateDirty;

        /// <summary>
        /// Initializes a new WorldState.
        /// </summary>
        /// <param name="commandRegister">The command register.</param>
        /// <param name="artifacts">The artifact center.</param>
        /// <param name="store">The store. Can not be null.</param>
        /// <param name="worldName">The world name.</param>
        /// <param name="isPublicWorld">Whether this world is public or private.</param>
        /// <param name="localFeedProvider">Local feed provider. Can not be null. (Required for the Zro builder.)</param>
        /// <param name="appLife">Application lifetime controller.</param>
        public WorldState(
            CommandRegister commandRegister,
            ArtifactCenter artifacts,
            IWorldStore store,
            IWorldName worldName,
            bool isPublicWorld,
            IEnvLocalFeedProvider localFeedProvider,
            IActivityMonitorFilteredClient userMonitorClient,
            IBasicApplicationLifetime appLife )
        {
            _artifacts = artifacts ?? throw new ArgumentNullException( nameof( artifacts ) );
            _store = store ?? throw new ArgumentNullException( nameof( store ) );
            WorldName = worldName ?? throw new ArgumentNullException( nameof( worldName ) );
            _localFeedProvider = localFeedProvider ?? throw new ArgumentNullException( nameof( localFeedProvider ) );
            _userMonitorClient = userMonitorClient;
            IsPublicWorld = isPublicWorld;
            _appLife = appLife;
            _solutionDrivers = new HashSet<ISolutionDriver>();
            _perBranchContext = new Dictionary<string, WorldBranchContext>();
            _cacheBySolutionName = new Dictionary<string, ISolutionDriver>();
            _gitRepositories = new HashSet<IGitRepository>();

            CommandProviderName = "World";
            commandRegister.Register( this );
        }

        public NormalizedPath CommandProviderName { get; }

        bool IsInitialized => _rawState != null;

        SolutionContext ISolutionDriverWorld.Register( ISolutionDriver driver )
        {
            if( !_solutionDrivers.Add( driver ) ) throw new InvalidOperationException( "Already registered." );
            if( !_perBranchContext.TryGetValue( driver.BranchName, out var c ) )
            {
                c = new WorldBranchContext( driver.BranchName );
                _perBranchContext.Add( driver.BranchName, c );
            }
            if( _gitRepositories.Add( driver.GitRepository ) )
            {
                UpdateGlobalGitStatus();
            }
            return c.OnRegisterDriver( driver );
        }

        void ISolutionDriverWorld.Unregister( ISolutionDriver driver )
        {
            if( !_solutionDrivers.Remove( driver ) ) throw new InvalidOperationException( "Not registered." );
            foreach( var k in _cacheBySolutionName.Where( kv => kv.Value == driver ).Select( kv => kv.Key ).ToList() )
            {
                _cacheBySolutionName.Remove( k );
            }
            _perBranchContext[driver.BranchName].OnUnregisterDriver( driver );

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
        /// Gets whether this world is public.
        /// </summary>
        public bool IsPublicWorld { get; }

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
        /// Initializes this world state by loading the local state.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Initialize( IActivityMonitor monitor )
        {
            bool alreadyInitialized = _rawState != null;
            if( !alreadyInitialized )
            {
                _rawState = _store.GetOrCreateLocalState( monitor, WorldName );
                LogFilter.TryParse( (string)_rawState.GeneralState.Attribute( "UserLogFilter" ) ?? "", out var logFilter );
                DoSetUserLogFilter( monitor, logFilter, false );
                SetReadonlyState();
                _rawState.Document.Changed += RawStateChanged;
            }
            return true;
        }

        /// <summary>
        /// Asks this world state to be dumped (in the monitor or anywhere lese) by
        /// raising <see cref="DumpWorldStatus"/> event.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod( confirmationRequired: false )]
        public bool DumpWorldState( IActivityMonitor monitor )
        {
            return RunSafe( monitor, "World Status.", ( m, error ) =>
            {
                var ev = new EventMonitoredArgs( monitor );
                DumpWorldStatus?.Invoke( this, ev );
            } );
        }

        /// <summary>
        /// Gets the registered solution drivers.
        /// </summary>
        public IReadOnlyCollection<ISolutionDriver> SolutionDrivers => _solutionDrivers;

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
            _cachedGlobalGitStatus = StandardGitStatus.Unknown;
            foreach( var r in _gitRepositories )
            {
                if( r.StandardGitStatus == StandardGitStatus.Unknown ) return _cachedGlobalGitStatus = StandardGitStatus.Unknown;
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
        /// Gets the mutable <see cref="XElement"/> general state.
        /// This is where state information are stored.
        /// </summary>
        public XElement GeneralState => _rawState?.GeneralState;

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

        [CommandMethod(confirmationRequired: false)]
        public void SetUserLogFilter( IActivityMonitor m, LogFilter filter ) => DoSetUserLogFilter( m, filter, true );
        
        void DoSetUserLogFilter( IActivityMonitor m, LogFilter filter, bool saveOnChange )
        {
            if( _userMonitorClient.MinimalFilter != filter )
            {
                _userMonitorClient.MinimalFilter = filter;
                _rawState.GeneralState.SetAttributeValue( "UserLogFilter", filter.ToString() );
                m.UnfilteredLog( ActivityMonitor.Tags.Empty, LogLevel.Info, $"User Log Filter level set to '{filter}' (actual filter is '{m.ActualFilter}').", m.NextLogTime(), null );
                if( saveOnChange ) Save( m );
            }
        }

        string GetCleanBranchName( IActivityMonitor monitor )
        {
            if( !CheckGlobalGitStatusLocalXorDevelop( monitor ) ) return null;
            bool allClean = true;
            foreach( var g in _gitRepositories )
            {
                allClean &= g.CheckCleanCommit( monitor );
            }
            if( !allClean ) return null;
            return CachedGlobalGitStatus == StandardGitStatus.Local
                            ? WorldName.LocalBranchName
                            : WorldName.DevelopBranchName;
        }

        /// <summary>
        /// Gets an up to date <see cref="IWorldSolutionContext"/> after having checked that
        /// repositories are all on 'local' or 'develop' branch.
        /// <see cref="CachedGlobalGitStatus"/> is up-to-date after this call.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="reloadSolutions">True to force a reload of the solutions.</param>
        /// <returns>The context or null on error.</returns>
        IWorldSolutionContext GetSolutionDependencyContext( IActivityMonitor monitor, bool reloadSolutions = false )
        {
            var branchName = GetCleanBranchName( monitor );
            if( branchName == null ) return null;
            if( !_perBranchContext.TryGetValue( branchName, out var c ) )
            {
                throw new Exception( $"No solution context available for branch {branchName}. GitBranchPlugins are not initialized or a ISolutionDriver plugin implementation is missing." );
            }
            return c.Refresh( monitor, reloadSolutions ) ? c : null;
        }

        /// <summary>
        /// Gets whether the <see cref="WorkStatus"/> is not <see cref="GlobalWorkStatus.Idle"/>
        /// nor <see cref="GlobalWorkStatus.WaitingReleaseConfirmation"/>.
        /// </summary>
        public bool IsConcludeCurrentWorkEnabled => IsInitialized
                                                    && WorkStatus != GlobalWorkStatus.Idle
                                                    && WorkStatus != GlobalWorkStatus.WaitingReleaseConfirmation;

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
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.Unknown, not: true ) ) return false;
            if( !SetWorkStatusAndSave( monitor, GlobalWorkStatus.SwitchingToLocal ) ) return false;
            return DoSwitchToLocal( monitor );
        }

        bool DoSwitchToLocal( IActivityMonitor monitor )
        {
            Debug.Assert( WorkStatus == GlobalWorkStatus.SwitchingToLocal );
            return RunSafe( monitor, $"Switching to {WorldName.LocalBranchName}.", ( m, error ) =>
            {
                foreach( var g in _gitRepositories )
                {
                    if( !g.SwitchDevelopToLocal( m, autoCommit: true ) ) return;
                    if( _appLife.StopRequested( m ) )
                    {
                        UpdateGlobalGitStatus();
                        return;
                    }
                }
                UpdateGlobalGitStatus();
                Debug.Assert( CachedGlobalGitStatus == StandardGitStatus.Local );

                var depContext = GetSolutionDependencyContext( m );
                if( depContext == null ) return;

                if( ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext, _appLife ) == null ) return;

                if( !error() )
                {
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
                var depContext = GetSolutionDependencyContext( m );
                if( depContext == null ) return;
                ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext, _appLife );
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
        public bool AllBuild( IActivityMonitor monitor, bool rebuildAll = false, bool withUnitTest = true )
        {
            if( !CanAllBuild ) throw new InvalidOperationException( nameof( CanAllBuild ) );
            if( !CheckGlobalGitStatusLocalXorDevelop( monitor ) ) return false;
            return RunSafe( monitor, $"Local build.", ( m, error ) =>
            {
                var ctx = GetSolutionDependencyContext( m );
                if( ctx == null ) return;

                ZeroBuilder zBuilder = ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, ctx, _appLife );
                if( zBuilder == null ) return;

                ctx = GetSolutionDependencyContext( m );
                if( ctx == null ) return;

                Builder builder = CachedGlobalGitStatus == StandardGitStatus.Local
                                ? (Builder)new LocalBuilder( zBuilder, _artifacts, _localFeedProvider, ctx, withUnitTest )
                                : new DevelopBuilder( zBuilder, _artifacts, _localFeedProvider, ctx, withUnitTest );
                RunBuild( m, builder, rebuildAll );
            } );
        }

        bool RunBuild( IActivityMonitor m, Builder b, bool forceRebuild )
        {
            var result = b.Run( m, forceRebuild );
            if( result == null ) return false;
            using( m.OpenInfo( $"Updating {result.Type} build result with {result.GeneratedArtifacts.Count} artifacts." ) )
            {
                foreach( var a in result.GeneratedArtifacts.GroupBy( a => a.Artifact.Artifact.Type ) )
                {
                    m.Info( $"{a.Key} => {a.Select( p => p.Artifact.Artifact.Name + '/' + p.Artifact.Version + " -> " + p.TargetName ).Concatenate()}" );
                }
            }
            _rawState.SetBuildResult( result );
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
                foreach( var g in _gitRepositories )
                {
                    if( !g.SwitchLocalToDevelop( m ) ) return;
                }
                var depContext = GetSolutionDependencyContext( m );
                if( depContext == null ) return;

                var zBuilder = ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext, _appLife );
                if( zBuilder == null ) return;

                depContext = GetSolutionDependencyContext( m );
                if( depContext == null ) return;

                var builder = new DevelopBuilder( zBuilder, _artifacts, _localFeedProvider, depContext, false );
                if( !RunBuild( m, builder, false ) ) return;

                if( !error() )
                {
                    SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
                }
            } );
        }

        ReleaseRoadmap LoadRoadmap( IActivityMonitor monitor )
        {
            var depContext = GetSolutionDependencyContext( monitor );
            if( depContext == null ) return null;
            var previous = GeneralState.EnsureElement( "Roadmap" );
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

        ReleaseRoadmap DoEditRoadmap( IActivityMonitor monitor, bool forgetAllExistingRoadmapVersions )
        {
            var roadmap = LoadRoadmap( monitor );
            if( roadmap == null ) return null;
            bool editSucceed = roadmap.UpdateRoadmap( monitor, VersionSelector, forgetAllExistingRoadmapVersions );
            // Always saves state to preserve any change in the roadmap, even on error.
            GeneralState.ReplaceElementByName( roadmap.ToXml() );
            if( !Save( monitor ) || !editSucceed ) return null;
            return roadmap;
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/>, <see cref="VersionSelector"/>
        /// and <see cref="CachedGlobalGitStatus"/> is on <see cref="StandardGitStatus.Develop"/>.
        /// </summary>
        public bool CanRelease => VersionSelector != null
                                  && WorkStatus == GlobalWorkStatus.Idle
                                  && CachedGlobalGitStatus == StandardGitStatus.Develop;

        /// <summary>
        /// Starts a release after an optional pull, using the current <see cref="VersionSelector"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="pull">Pull all branches first.</param>
        /// <param name="resetRoadmap">True to forget the current roadmap (it if exists) and ask for each and every version.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Release( IActivityMonitor monitor, bool pull = true, bool resetRoadmap = false )
        {
            if( !CanRelease ) throw new InvalidOperationException( nameof( CanRelease ) );
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.Develop ) ) return false;

            bool reloadNeeded = false;
            foreach( var g in _gitRepositories )
            {
                if( !g.CheckCleanCommit( monitor ) ) return false;
                if( pull )
                {
                    var (Success, ReloadNeeded) = g.Pull( monitor );
                    if( !Success ) return false;
                    reloadNeeded |= ReloadNeeded;
                }
            }
            if( reloadNeeded && GetSolutionDependencyContext( monitor, true ) == null ) return false;

            var roadmap = DoEditRoadmap( monitor, resetRoadmap );
            if( roadmap == null ) return false;

            SetWorkStatus( GlobalWorkStatus.Releasing );
            if( !Save( monitor ) ) return false;

            return DoReleasing( monitor );
        }

        bool DoReleasing( IActivityMonitor monitor )
        {
            return RunSafe( monitor, $"Starting Release.", ( m, error ) =>
            {
                bool firstRun = GeneralState.Element( "GitSnapshot" ) == null;
                if( firstRun )
                {
                    using( m.OpenInfo( $"First run: capturing {WorldName.DevelopBranchName} and {WorldName.MasterBranchName} branches positions." ) )
                    {
                        var snapshot = new XElement( "GitSnapshot",
                                                 _gitRepositories.Select( g => new XElement( "G",
                                                            new XAttribute( "P", g.SubPath ),
                                                            new XAttribute( "D", g.GetBranchSha( m, WorldName.DevelopBranchName ) ),
                                                            new XAttribute( "M", g.GetBranchSha( m, WorldName.MasterBranchName ) ?? "" ) ) ) );
                        GeneralState.Add( snapshot );
                        if( !Save( m ) ) return;
                        if( !_localFeedProvider.Release.RemoveAll( m ) ) return;
                    }
                }

                var depContext = GetSolutionDependencyContext( m );
                if( depContext == null ) return;

                ZeroBuilder zBuilder = ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext, _appLife );
                if( zBuilder == null ) return;

                var roadmap = LoadRoadmap( monitor );
                if( roadmap == null || !roadmap.IsValid )
                {
                    monitor.Error( $"Road map is invalid. Current release should be cancelled." );
                    return;
                }

                var b = new ReleaseBuilder( zBuilder, _artifacts, roadmap, _localFeedProvider );
                if( !RunBuild( m, b, firstRun ) ) return;

                if( !error() )
                {
                    SetWorkStatusAndSave( m, GlobalWorkStatus.WaitingReleaseConfirmation );
                }
            } );
        }

        /// <summary>
        /// Gets whether <see cref="CancelRelease"/> can be called.
        /// </summary>
        public bool CanCancelRelease => WorkStatus == GlobalWorkStatus.Releasing
                                        || WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation;


        /// <summary>
        /// Cancel the current release.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool CancelRelease( IActivityMonitor m )
        {
            if( !CanCancelRelease ) throw new InvalidOperationException( nameof( CanCancelRelease ) );
            return SetWorkStatusAndSave( m, GlobalWorkStatus.CancellingRelease ) && ConcludeCurrentWork( m );
        }

        bool DoCancellingRelease( IActivityMonitor monitor )
        {
            return RunSafe( monitor, $"Cancelling current Release.", ( m, error ) =>
            {
                if( !HandleReleaseVersionTags( m, false ) ) return;
                var snapshot = GeneralState.Element( "GitSnapshot" );
                if( snapshot != null )
                {
                    using( m.OpenInfo( $"Restoring '{WorldName.DevelopBranchName}' and '{WorldName.MasterBranchName}' branches positions." ) )
                    {
                        foreach( var e in snapshot.Elements() )
                        {
                            var path = (string)e.AttributeRequired( "P" );
                            var git = _gitRepositories.FirstOrDefault( g => g.SubPath == path );
                            if( git == null )
                            {
                                m.Error( $"Unable to find Git repository for {path}." );
                                return;
                            }
                            if( !git.ResetBranchState( m, WorldName.MasterBranchName, (string)e.AttributeRequired( "M" ) )
                                || !git.ResetBranchState( m, WorldName.DevelopBranchName, (string)e.AttributeRequired( "D" ) ) )
                            {
                                return;
                            }
                        }
                    }
                    snapshot.Remove();
                }
                GetSolutionDependencyContext( m, true );
                _localFeedProvider.Release.RemoveAll( m );
                if( !error() )
                {
                    SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
                }
            } );
        }

        bool HandleReleaseVersionTags( IActivityMonitor m, bool pushVersionTagsAndBranches )
        {
            bool success = true;
            using( m.OpenInfo( pushVersionTagsAndBranches
                                ? "Pushing version tags and branches."
                                : "Clearing version tags." ) )
            {
                var versions = ReleaseRoadmap.Load( GeneralState.Element( "Roadmap" ) )
                                           .Select( e => (e.SubPath, e.Info, Git: _gitRepositories.FirstOrDefault( g => e.SubPath.StartsWith( g.SubPath ) ) ) );
                foreach( var (SubPath, Info, Git) in versions )
                {
                    if( Git == null )
                    {
                        m.Fatal( $"Unable to find Git repository for {SubPath} from current Roadmap." );
                        return false;
                    }
                    if( pushVersionTagsAndBranches )
                    {
                        if( Info.Level != ReleaseLevel.None )
                        {
                            success &= Git.PushVersionTag( m, Info.Version );
                            // Since we have a version: we may be on master.
                            if( success && Info.Version.PackageQuality == PackageQuality.Release )
                            {
                                success &= Git.Push( m, WorldName.MasterBranchName );
                            }
                        }
                        // Always push develop even when Level = None since
                        // build project dependencies may have been upgraded.
                        if( success )
                        {
                            success &= Git.Push( m, WorldName.DevelopBranchName );
                        }
                    }
                    else
                    {
                        if( Info.Level != ReleaseLevel.None )
                        {
                            success &= Git.ClearVersionTag( m, Info.Version );
                        }
                    }
                }
            }
            return success;
        }

        /// <summary>
        /// Release can be published when <see cref="GlobalWorkStatus.WaitingReleaseConfirmation"/>.
        /// </summary>
        public bool CanPublishRelease => WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation;

        [CommandMethod]
        public bool PublishRelease( IActivityMonitor m )
        {
            if( !CanPublishRelease ) throw new InvalidOperationException( nameof( CanPublishRelease ) );
            return DoPublishingRelease( m );
        }

        /// <summary>
        /// CI can be published when <see cref="GlobalWorkStatus.Idle"/> and a CI build result is available.
        /// </summary>
        public bool CanPublishCI => WorkStatus == GlobalWorkStatus.Idle
                                    && _rawState.GetBuildResult( BuildResultType.CI ) != null;

        [CommandMethod]
        public bool PublishCI( IActivityMonitor monitor )
        {
            if( !CanPublishCI ) throw new InvalidOperationException( nameof( CanPublishCI ) );
            return RunSafe( monitor, $"Publishing CI.", ( m, error ) =>
            {
                var buildResults = _rawState.GetBuildResult( BuildResultType.CI );
                if( !DoPushArtifacts( m, buildResults ) ) return;
                bool success = true;
                foreach( var g in _gitRepositories )
                {
                    success &= g.Push( m, WorldName.DevelopBranchName );
                }
                if( !success ) return;
                _rawState.PublishBuildResult( buildResults.Type );
                Save( m );
            } );
        }

        bool DoPublishingRelease( IActivityMonitor monitor )
        {
            return RunSafe( monitor, $"Publishing Release.", ( m, error ) =>
            {
                var buildResults = _rawState.GetBuildResult( BuildResultType.Release );
                if( buildResults != null && !DoPushArtifacts( m, buildResults ) ) return;
                if( !HandleReleaseVersionTags( m, true ) ) return;
                _rawState.PublishBuildResult( buildResults.Type );
                if( !error() )
                {
                    GeneralState.Element( "GitSnapshot" )?.Remove();
                    SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
                }
            } );
        }

        bool DoPushArtifacts( IActivityMonitor monitor, BuildResult buildResults )
        {
            Debug.Assert( buildResults.Type == BuildResultType.Release || buildResults.Type == BuildResultType.CI );
            var local = _localFeedProvider.GetFeed( buildResults.Type );
            return RunSafe( monitor, $"Publishing Artifacts from local '{local.PhysicalPath}'.", ( m, error ) =>
            {
                foreach( var a in buildResults.GeneratedArtifacts.GroupBy( a => a.TargetName ) )
                {
                    using( m.OpenInfo( $"Publishing to target: {a.Key}." ) )
                    {
                        var h = _artifacts.Find( a.Key );
                        if( !local.PushLocalArtifacts( m, h, a.Select( p => p.Artifact ) ) )
                        {
                            Debug.Assert( error(), "An error must have been logged." );
                            return;
                        }
                    }
                }
            } );
        }

        public bool CanGenerateLTSWorld => WorldName.LTSKey == null && !IsDirty;

        [CommandMethod( confirmationRequired: true )]
        public bool GenerateLTSWorld( IActivityMonitor m, string LTSKey )
        {
            if( !CanGenerateLTSWorld )
            {
                if( !CanGenerateLTSWorld ) throw new InvalidOperationException( nameof( CanGenerateLTSWorld ) );
            }
            return CreateParallelWorld( m, LTSKey );
        }


        public bool CreateParallelWorld( IActivityMonitor m, string branchName )
        {
            m.Info( "Fetching all branches." );
            foreach( var repo in _gitRepositories )
            {
                if( !repo.FetchBranches( m ) )
                {
                    m.Error( "Could not fetch branches, aborting" );
                    return false;
                }
            }
            var worldData = _store.ReadWorldDescription( m, WorldName );
            var worldRoot = worldData.Root;
            string newWorldName = WorldName.Name + "-" + branchName + "-World";
            worldRoot.Name = newWorldName;
            using( m.OpenInfo( $"Changing XML root element from '{worldRoot.Name} to {newWorldName}" ) )
            {
                foreach( XElement branch in worldData.Root.Descendants( "Branch" ) )
                {
                    XAttribute oldBranchAttribute = branch.Attribute( "Name" );
                    string oldBranchName = oldBranchAttribute?.Value;
                    if( oldBranchAttribute != null
                        && (oldBranchName == "master"
                            || oldBranchName == "develop"
                            || oldBranchName.StartsWith( "master-" )
                            || oldBranchName.StartsWith( "develop-" )
                           )
                      )
                    {
                        string newName = oldBranchName + "-" + branchName;
                        string repoUri = branch.Parent.Attribute( "Url" ).Value;
                        var repo = _gitRepositories.Single( p => StringComparer.OrdinalIgnoreCase.Equals( repoUri, p.OriginUrl ) );
                        m.Info( "Creating branch " + newName );
                        repo.CreateBranch( m, newName );
                        m.Info( "Pushing branch" );
                        repo.Push( m, newName );
                        m.Info( $"Changing Branch from {oldBranchName} to {newName}" );
                        oldBranchAttribute.Value = newName;
                    }
                }
            }
            m.Info( $"Creating new World {newWorldName}" );
            _store.CreateNew( m, WorldName.Name, branchName, worldData );
            return true;
        }

        #region Read only State

        StandardGitStatus _roGlobalGitStatus;
        GlobalWorkStatus _roGlobalWorkStatus;
        XElement _roGeneralState;

        void SetReadonlyState()
        {
            _roGlobalGitStatus = CachedGlobalGitStatus;
            _roGlobalWorkStatus = _rawState.WorkStatus;
            _roGeneralState = new XElement( _rawState.GeneralState );
            _roGeneralState.Changing += PreventChanges;
        }

        static void PreventChanges( object sender, XObjectChangeEventArgs e )
        {
            throw new InvalidOperationException( "XElement is read-only." );
        }

        StandardGitStatus IWorldState.GlobalGitStatus => _roGlobalGitStatus;

        GlobalWorkStatus IWorldState.WorkStatus => _roGlobalWorkStatus;

        XElement IWorldState.GeneralState => _roGeneralState;

        #endregion

    }

}
