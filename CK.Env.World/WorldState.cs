using CK.Core;
using CK.Text;
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
    public partial class WorldState : IWorldState, ICommandMethodsProvider
    {
        readonly IWorldStore _store;
        readonly IDependentSolutionContextLoader _solutionContextLoader;
        readonly ILocalBuildProjectZeroBuilder _buildProjectZeroBuilder;
        readonly List<ISolutionDriver> _solutionDrivers;
        readonly List<IGitRepository> _gitRepositories;
        RawXmlWorldState _rawState;
        Roadmap _roadMap;
        StandardGitStatus _cachedGlobalGitStatus;
        bool _isStateDirty;

        /// <summary>
        /// Initializes a new WorldState.
        /// </summary>
        /// <param name="store">The store. Can not be null.</param>
        /// <param name="solutionContextLoader">Loader of solution dependecy context.</param>
        public WorldState(
            CommandRegister commandRegister,
            IWorldStore store,
            IWorldName worldName,
            IDependentSolutionContextLoader solutionContextLoader,
            ILocalBuildProjectZeroBuilder buildProjectZeroBuilder )
        {
            if( store == null ) throw new ArgumentNullException( nameof( store ) );
            if( worldName == null ) throw new ArgumentNullException( nameof( worldName ) );
            if( solutionContextLoader == null ) throw new ArgumentNullException( nameof( solutionContextLoader ) );
            if( buildProjectZeroBuilder == null ) throw new ArgumentNullException( nameof( buildProjectZeroBuilder ) );
            _store = store;
            WorldName = worldName;
            _solutionContextLoader = solutionContextLoader;
            _buildProjectZeroBuilder = buildProjectZeroBuilder;
            Debug.Assert( ((int[])Enum.GetValues( typeof( GlobalWorkStatus ) )).SequenceEqual( Enumerable.Range( 0, 7 ) ) );
            _roWorkState = new XElement[7];
            _solutionDrivers = new List<ISolutionDriver>();
            _gitRepositories = new List<IGitRepository>();

            CommandProviderName = "Solutions";
            commandRegister.Register( this );
        }

        public NormalizedPath CommandProviderName { get; }

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
        /// Gets or sets whether cache is disabled: when cache is disabled, <see cref="Initialize"/> is called
        /// with "force = true" before each operation.
        /// Defaults to false.
        /// </summary>
        public bool NoCache { get; set; }

        /// <summary>
        /// Initializes or reinitializes this world state.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="force">
        /// True to force a reinitialization, false to avoid any reinitialization (if already initialized).
        /// and null (the default) to rely on <see cref="NoCache"/> setting.
        /// </param>
        /// <returns>True on success, false on error.</returns>
        public bool Initialize( IActivityMonitor monitor, bool? force = null )
        {
            if( _rawState == null )
            {
                _rawState = _store.GetOrCreateLocalState( monitor, WorldName );
                SetReadonlyState();
                _rawState.Document.Changed += RawStateChanged;
            }
            if( !force.HasValue ) force = NoCache;
            if( _solutionDrivers.Count > 0 && !force.Value ) return false;
            return RunSafe( monitor, "Initializing World.", (m,error) =>
            {
                var ev = new InitializingEventArgs( monitor );
                Initializing?.Invoke( this, ev );
                _solutionDrivers.Clear();
                _solutionDrivers.AddRange( ev.SolutionsDrivers );
                _gitRepositories.Clear();
                _gitRepositories.AddRange( _solutionDrivers.Select( s => s.GitRepository ).Distinct() );
                if( !error() ) Initialized?.Invoke( this, ev );
            } );
        }

        /// <summary>
        /// Gets the solution drivers.
        /// Empty until <see cref="Initialize"/> has been called.
        /// </summary>
        public IReadOnlyList<ISolutionDriver> SolutionDrivers => _solutionDrivers;

        bool RunSafe( IActivityMonitor m, string message, Action<IActivityMonitor,Func<bool>> action )
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
                    m.Error( $"Executing: {message}", ex );
                }
                if( !result ) m.CloseGroup( "Failed." );
            }
            return result;
        }

        /// <summary>
        /// Computes the current git status that applies to the whole world
        /// and updates the <see cref="CachedGlobalGitStatus"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="forceInitialize">True to force a re intialization (see <see cref="Initialize"/>).</param>
        /// <returns>The standard Git status.</returns>
        public StandardGitStatus GetGlobalGitStatus( IActivityMonitor monitor, bool? forceInitialize = null )
        {
            _cachedGlobalGitStatus = StandardGitStatus.Unknwon;
            if( !Initialize( monitor, forceInitialize ) || _gitRepositories.Count == 0 ) return _cachedGlobalGitStatus;
            foreach( var r in _gitRepositories )
            {
                if( r.StandardGitStatus == StandardGitStatus.Unknwon ) return _cachedGlobalGitStatus = StandardGitStatus.Unknwon;
                if( _cachedGlobalGitStatus == StandardGitStatus.Unknwon ) _cachedGlobalGitStatus = r.StandardGitStatus;
                else if( _cachedGlobalGitStatus != r.StandardGitStatus ) return _cachedGlobalGitStatus = StandardGitStatus.Unknwon;
            }
            return _cachedGlobalGitStatus;
        }

        /// <summary>
        /// Calls <see cref="GetGlobalGitStatus"/> and checks the curent Git status.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="expected">Expected or rejected (depends on <paramref name="not"/>).</param>
        /// <param name="not">True to consider the <paramref name="expected"/> to actually be rejected.</param>
        /// <param name="forceInitialize">True to force a re intialization (see <see cref="Initialize"/>).</param>
        /// <returns>True if the expected/rejected Git status match.</returns>
        public bool CheckGlobalGitStatus( IActivityMonitor monitor, StandardGitStatus expected, bool not = false, bool? forceInitialize = null )
        {
            var s = GetGlobalGitStatus( monitor, forceInitialize );
            if( not ? s == expected : s != expected )
            {
                if( not ) monitor.Error( $"Expected GlobalGitStatus to not be '{expected}'." );
                else monitor.Error( $"Expected GlobalGitStatus to be '{expected}' but it is '{s}'." );
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
        /// </summary>
        public GlobalWorkStatus WorkStatus => _rawState.WorkStatus;

        /// <summary>
        /// Gets the operation name (when <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.OtherOperation"/>).
        /// </summary>
        public string OtherOperationName => _rawState.OtherOperationName;

        /// <summary>
        /// Gets the mutable <see cref="XElement"/> general state.
        /// This is where state information that are not specific to an operation are stored.
        /// </summary>
        public XElement GeneralState => _rawState.GeneralState;

        /// <summary>
        /// Gets the mutable <see cref="XElement"/> state for an operation.
        /// </summary>
        /// <param name="status">The work status.</param>
        /// <returns>The state element.</returns>
        public XElement GetWorkState( GlobalWorkStatus status ) => _rawState.GetWorkState( status );

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
        /// Gets whether the <see cref="WorkStatus"/> is not <see cref="GlobalWorkStatus.Idle"/> nor <see cref="GlobalWorkStatus.WaitingReleaseConfirmation"/>
        /// and <see cref="CachedGlobalGitStatus"/> is not <see cref="StandardGitStatus.Unknwon"/>.
        /// </summary>
        public bool IsConcludeCurrentWorkEnabled => WorkStatus != GlobalWorkStatus.Idle
                                                    && WorkStatus != GlobalWorkStatus.WaitingReleaseConfirmation;

        /// <summary>
        /// Concludes the current work.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
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
        /// is not already on <see cref="StandardGitStatus.LocalBranch"/>.
        /// </summary>
        public bool CanSwitchToLocal => WorkStatus == GlobalWorkStatus.Idle
                                        && CachedGlobalGitStatus != StandardGitStatus.LocalBranch;

        /// <summary>
        /// Switches from develop to local branch.
        /// Must throw an <see cref="InvalidOperationException"/> if <see cref="CanSwitchToLocal"/> is false.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="compactCommitMode">Compact commit mode.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool SwitchToLocal( IActivityMonitor monitor, bool compactCommitMode = true )
        {
            if( !CanSwitchToLocal ) throw new InvalidOperationException( nameof( CanSwitchToLocal ) );
            if( GetGlobalGitStatus( monitor ) == StandardGitStatus.LocalBranch ) return true;
            SetWorkStatus( GlobalWorkStatus.SwitchingToLocal );
            GetWorkState( GlobalWorkStatus.SwitchingToLocal ).SetAttributeValue( "CompactCommitMode", compactCommitMode );
            if( !Save( monitor ) ) return false;
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
                var depContext = _solutionContextLoader.Load( m, _gitRepositories, WorldName.LocalBranchName, NoCache );
                if( depContext == null ) return;

                if( !_buildProjectZeroBuilder.LocalZeroBuildProjects( m, depContext ) ) return;

                if( !error() ) SwitchedToLocal?.Invoke( this, ev );
                SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
            } );
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/> and <see cref="CachedGlobalGitStatus"/>
        /// is not on <see cref="StandardGitStatus.DevelopBranch"/>.
        /// </summary>
        public bool CanSwitchToDevelop => WorkStatus == GlobalWorkStatus.Idle
                                          && CachedGlobalGitStatus != StandardGitStatus.DevelopBranch;

        /// <summary>
        /// Switches back from local to develop branch.
        /// Must throw an <see cref="InvalidOperationException"/> if <see cref="CanSwitchToDevelop"/> is false.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchToDevelop( IActivityMonitor monitor )
        {
            if( !CanSwitchToDevelop ) throw new InvalidOperationException( nameof( CanSwitchToDevelop ) );
            if( GetGlobalGitStatus( monitor ) == StandardGitStatus.DevelopBranch ) return true;
            SetWorkStatus( GlobalWorkStatus.SwitchingToDevelop );
            if( !Save( monitor ) ) return false;
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


                if( !error() ) SwitchedToDevelop?.Invoke( this, ev );
                SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
            } );
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/> and <see cref="CachedGlobalGitStatus"/>
        /// is on <see cref="StandardGitStatus.DevelopBranch"/>.
        /// </summary>
        public bool CanRelease => WorkStatus == GlobalWorkStatus.Idle
                                  && CachedGlobalGitStatus == StandardGitStatus.DevelopBranch;

        /// <summary>
        /// Gets or creates the current roadmap that can be modified.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The roadmap.</returns>
        public Roadmap EnsureRoadmap( IActivityMonitor monitor, bool reload = false )
        {
            if( !CanRelease ) throw new InvalidOperationException( nameof( CanRelease ) );
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.DevelopBranch ) ) return null;
            return DoEnsureRoadmap( monitor, NoCache || reload );
        }

        Roadmap DoEnsureRoadmap( IActivityMonitor monitor, bool reload )
        {
            if( _roadMap != null && !reload ) return _roadMap;
            var depContext = _solutionContextLoader.Load( monitor, _gitRepositories, WorldName.DevelopBranchName, NoCache || reload );
            if( depContext == null ) return null;
            var previous = _roadMap != null ? _roadMap.ToXml() : GetWorkState( GlobalWorkStatus.Releasing ).Element( "RoadMap" );
            return _roadMap = Roadmap.Create( monitor, depContext, previous );
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
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.DevelopBranch ) ) return false;

            bool reloadNeeded = NoCache;
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

        bool DoReleasing( IActivityMonitor monitor, Roadmap roadmap = null )
        {
            if( roadmap == null )
            {
                roadmap = DoEnsureRoadmap( monitor, NoCache );
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
        public bool CanCancelRelease => WorkStatus == GlobalWorkStatus.Releasing || WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation;

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
        public bool CanPublishRelease => WorkStatus == GlobalWorkStatus.WaitingReleaseConfirmation;

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
            for( int i = 0; i < 7; i++ )
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
