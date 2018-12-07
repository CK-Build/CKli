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
    public partial class WorldState : IWorldState, ICommandMethodsProvider
    {
        readonly IWorldStore _store;
        readonly IDependentSolutionContextLoader _solutionContextLoader;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly List<ISolutionDriver> _solutionDrivers;
        readonly Dictionary<string,ISolutionDriver> _cacheBySolutionName;
        readonly List<IGitRepository> _gitRepositories;
        RawXmlWorldState _rawState;
        Roadmap _roadMap;
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
            _solutionDrivers = new List<ISolutionDriver>();
            _cacheBySolutionName = new Dictionary<string, ISolutionDriver>();
            _gitRepositories = new List<IGitRepository>();

            CommandProviderName = "World";
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


        bool IsInitialized => _solutionDrivers.Count > 0;

        public bool IsInitializeEnabled => !IsInitialized;

        [CommandMethod]
        public bool Initialize( IActivityMonitor m ) => Initialize( m, NoCache );

        /// <summary>
        /// Initializes or reinitializes this world state.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="force">
        /// True to force a reinitialization, false to avoid any reinitialization (if already initialized).
        /// and null to rely on <see cref="NoCache"/> setting.
        /// </param>
        /// <returns>True on success, false on error.</returns>
        public bool Initialize( IActivityMonitor monitor, bool? force )
        {
            if( _rawState == null )
            {
                _rawState = _store.GetOrCreateLocalState( monitor, WorldName );
                SetReadonlyState();
                _rawState.Document.Changed += RawStateChanged;
            }
            if( !force.HasValue ) force = NoCache;
            if( _solutionDrivers.Count > 0 && !force.Value ) return true;
            return RunSafe( monitor, "Initializing World.", ( m, error ) =>
            {
                var ev = new InitializingEventArgs( monitor );

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
                _cacheBySolutionName.Clear();
                _solutionDrivers.Clear();
                _solutionDrivers.AddRange( ev.SolutionsDrivers );
                _gitRepositories.Clear();
                _gitRepositories.AddRange( _solutionDrivers.Select( s => s.GitRepository ).Distinct() );
                if( !error() )
                {
                    Initialized?.Invoke( this, ev );
                    GetGlobalGitStatus( monitor, false );
                }
            } );
        }

        /// <summary>
        /// Gets the solution drivers.
        /// Empty until <see cref="Initialize"/> has been called.
        /// </summary>
        public IReadOnlyList<ISolutionDriver> SolutionDrivers => _solutionDrivers;

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
            if( !_cacheBySolutionName.TryGetValue( uniqueSolutionName, out var driver ) )
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
                        _cacheBySolutionName.Add( n, driver );
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
                if( IsInitialized ) GetGlobalGitStatus( m, false );
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
            if( Initialize( monitor, forceInitialize ) && _gitRepositories.Count > 0 )
            {
                foreach( var r in _gitRepositories )
                {
                    if( r.StandardGitStatus == StandardGitStatus.Unknwon ) return _cachedGlobalGitStatus = StandardGitStatus.Unknwon;
                    _cachedGlobalGitStatus |= r.StandardGitStatus;
                }
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
        /// Gets the <see cref="IDependentSolutionContext"/> after having called <see cref="GetGlobalGitStatus"/>
        /// and checked that repositories are all on 'local' or 'develop' branch.
        /// <see cref="CachedGlobalGitStatus"/> is up-to-date after this call.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="reloadSolutions">True to force a reload of the solutions.</param>
        /// <returns>The dependency context or null on error.</returns>
        IDependentSolutionContext GetSolutionDependentContext( IActivityMonitor monitor, bool reloadSolutions = false )
        {
            var s = GetGlobalGitStatus( monitor, NoCache );
            string branchName;
            if( s == StandardGitStatus.Local ) branchName = WorldName.LocalBranchName;
            else if( s == StandardGitStatus.Develop ) branchName = WorldName.DevelopBranchName;
            else
            {
                monitor.Error( $"Repositories must all be on '{WorldName.LocalBranchName}' or '{WorldName.DevelopBranchName}'." );
                return null;
            }
            return _solutionContextLoader.Load( monitor, _gitRepositories, branchName, NoCache | reloadSolutions );
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
        /// is not <see cref="StandardGitStatus.Unknwon"/> (ie. all branches are either 'local' or 'develop').
        /// </summary>
        public bool CanSwitchToLocal => IsInitialized
                                        && WorkStatus == GlobalWorkStatus.Idle
                                        && CachedGlobalGitStatus != StandardGitStatus.Unknwon;

        /// <summary>
        /// Switches from develop to local branch.
        /// Must throw an <see cref="InvalidOperationException"/> if <see cref="CanSwitchToLocal"/> is false.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="zeroBuildProjects">True to build the build projects.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool SwitchToLocal( IActivityMonitor monitor, bool zeroBuildProjects = false )
        {
            if( !CanSwitchToLocal ) throw new InvalidOperationException( nameof( CanSwitchToLocal ) );
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.Unknwon, not: true ) ) return false;
            if( !SetWorkStatusAndSave( monitor, GlobalWorkStatus.SwitchingToLocal ) ) return false;
            if( !DoSwitchToLocal( monitor ) ) return false;
            if( !zeroBuildProjects ) return true;
            var depContext = GetSolutionDependentContext( monitor );
            if( depContext == null ) return false;
            return DoZeroBuildProjects( monitor, depContext );
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
            var depContext = GetSolutionDependentContext( monitor );
            if( depContext == null ) return false;
            return DoZeroBuildProjects( monitor, depContext );
        }

        bool DoZeroBuildProjects( IActivityMonitor monitor, IDependentSolutionContext depContext )
        {
            Debug.Assert( depContext != null && depContext.UniqueBranchName != null );
            IEnvLocalFeed localFeed = _localFeedProvider.GetFeed( CachedGlobalGitStatus );
            return RunSafe( monitor, $"Fixing Build projects, using ZeroVersion in {depContext.UniqueBranchName}.", ( m, error ) =>
            {
                if( depContext.ZeroBuildProjects == null )
                {
                    m.Error( "Build Projects dependencies failed to be computed." );
                    return;
                }
                if( depContext.ZeroBuildProjects.Count == 0 )
                {
                    m.Info( "No Build Project exist." );
                    return;
                }
                var packageNames = depContext.ZeroBuildProjects.Where( p => p.MustPack ).Select( p => p.ProjectName );
                var packageNamesText = packageNames.Concatenate();

                using( m.OpenInfo( $"Removing {packageNamesText} packages ZeroVersion from local NuGet cache if they exist." ) )
                {
                    foreach( var pName in packageNames )
                    {
                        _localFeedProvider.RemoveFromNuGetCache( m, pName, SVersion.ZeroVersion );
                    }
                }
                using( m.OpenInfo( $"Building ZeroVersion projects." ) )
                {
                    var mustBuild = new HashSet<string>( depContext.ZeroBuildProjects.Select( z => z.FullName ) );
                    var memPath = localFeed.PhysicalPath.AppendPart( "CacheZeroVersion.txt" );
                    var sha1Cache = System.IO.File.Exists( memPath )
                                    ? System.IO.File.ReadAllLines( memPath )
                                                    .Select( l => l.Split() )
                                                    .Where( l => mustBuild.Contains( l[0] ) )
                                                    .ToDictionary( l => l[0], l => l[1] )
                                    : new Dictionary<string, string>();
                    m.Info( $"File '{memPath}' contains {sha1Cache.Count} entries." );

                    void SaveSha1Cache()
                    {
                        m.Info( $"Saving {sha1Cache.Count} entries in file '{memPath}'." );
                        System.IO.File.WriteAllLines( memPath, sha1Cache.Select( kv => kv.Key + ' ' + kv.Value ) );
                    }

                    foreach( var p in depContext.ZeroBuildProjects )
                    {
                        using( m.OpenInfo( $"{p} <= { (p.Dependencies.Count > 0 ? p.Dependencies.Concatenate() : "(no dependency)") }." ) )
                        {
                            var driver = FindSolutionDriver( monitor, p.SolutionName, depContext.UniqueBranchName );
                            var currentCommitSha = driver.GitRepository.HeadCommitSHA1;
                            if( sha1Cache.TryGetValue( p.FullName, out var sha )
                                && sha == currentCommitSha
                                && !p.Dependencies.Any( depName => mustBuild.Contains( depName ) ) )
                            {
                                mustBuild.Remove( p.FullName );
                                m.Info( $"Project '{p}' is up to date. Build skipped." );
                                continue;
                            }
                            if( !driver.ZeroBuildProject( m, p ) )
                            {
                                SaveSha1Cache();
                                return;
                            }
                            sha1Cache[p.FullName] = currentCommitSha;
                        }
                    }
                    SaveSha1Cache();
                }
            } );
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/> and <see cref="CachedGlobalGitStatus"/>
        /// is on <see cref="StandardGitStatus.Local"/>.
        /// </summary>
        public bool CanBuildAllLocal => WorkStatus == GlobalWorkStatus.Idle
                                        && CachedGlobalGitStatus == StandardGitStatus.Local;

        [CommandMethod]
        public bool BuildAllLocal( IActivityMonitor monitor, bool withUnitTest = true )
        {
            if( !CanBuildAllLocal ) throw new InvalidOperationException( nameof( CanBuildAllLocal ) );
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.Local ) ) return false;
            return RunSafe( monitor, $"Local build.", ( m, error ) =>
            {
                var depContext = GetSolutionDependentContext( m );
                if( depContext == null ) return;
                foreach( var s in depContext.Solutions )
                {
                    depContext.LogSolutions( m, s );
                    var d = FindSolutionDriver( m, s );
                    
                    if( !d.Build( m, true, withUnitTest ) ) return;
                }
            } );
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/> and <see cref="CachedGlobalGitStatus"/>
        /// is on <see cref="StandardGitStatus.Local"/> or on a mix of 'develop' and 'local' (<see cref="StandardGitStatus.DevelopOrLocalBranch"/>).
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
            var s = GetGlobalGitStatus( monitor, NoCache );
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
                var depContext = GetSolutionDependentContext( m );
                if( depContext == null ) return;

                if( !DoZeroBuildProjects( monitor, depContext ) ) return;

                foreach( var s in depContext.Solutions )
                {
                    depContext.LogSolutions( m, s );
                    var d = FindSolutionDriver( m, s );

                    throw new NotImplementedException();
                }

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
        public Roadmap EnsureRoadmap( IActivityMonitor monitor, bool reload = false )
        {
            if( !CanRelease ) throw new InvalidOperationException( nameof( CanRelease ) );
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.Develop ) ) return null;
            return DoEnsureRoadmap( monitor, NoCache || reload );
        }

        Roadmap DoEnsureRoadmap( IActivityMonitor monitor, bool reload )
        {
            if( _roadMap != null && !reload ) return _roadMap;
            var depContext = GetSolutionDependentContext( monitor, reload );
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
            if( !CheckGlobalGitStatus( monitor, StandardGitStatus.Develop ) ) return false;

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
