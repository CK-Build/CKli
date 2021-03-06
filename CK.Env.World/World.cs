using CK.Core;
using CK.Build;
using CK.Env.DependencyModel;
using CK.SimpleKeyVault;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Primary World object. Handles the solutions, the solution drivers, the local
    /// and shared states and supports numerous commands.
    /// </summary>
    public partial class World : ISolutionDriverWorld, ICommandMethodsProvider
    {
        readonly WorldStore _store;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly SecretKeyStore _keyStore;
        readonly ArtifactCenter _artifacts;

        readonly DriversCollection _solutionDrivers;
        readonly HashSet<IGitRepository> _gitRepositories;
        readonly IActivityMonitorFilteredClient _userMonitorClient;

        LocalWorldState _localState;
        SharedWorldState _sharedState;
        StandardGitStatus _cachedGlobalGitStatus;

        /// <summary>
        /// Initializes a new World.
        /// </summary>
        /// <param name="commandRegister">The command register.</param>
        /// <param name="artifacts">The artifact center.</param>
        /// <param name="store">The world store. Can not be null.</param>
        /// <param name="worldName">The world name.</param>
        /// <param name="isPublicWorld">Whether this world is public or private.</param>
        /// <param name="localFeedProvider">Local feed provider. Can not be null. (Required for the Zero builder.)</param>
        /// <param name="keyStore">User key store. Must not be null.</param>
        /// <param name="userMonitorClient">Used to set the log level.</param>
        /// <param name="appLife">Application lifetime controller.</param>
        public World(
            CommandRegister commandRegister,
            ArtifactCenter artifacts,
            WorldStore store,
            IRootedWorldName worldName,
            bool isPublicWorld,
            IEnvLocalFeedProvider localFeedProvider,
            SecretKeyStore keyStore,
            IActivityMonitorFilteredClient userMonitorClient )
        {
            _artifacts = artifacts ?? throw new ArgumentNullException( nameof( artifacts ) );
            _store = store ?? throw new ArgumentNullException( nameof( store ) );
            WorldName = worldName ?? throw new ArgumentNullException( nameof( worldName ) );
            _localFeedProvider = localFeedProvider ?? throw new ArgumentNullException( nameof( localFeedProvider ) );
            _keyStore = keyStore ?? throw new ArgumentNullException( nameof( keyStore ) );
            IsPublicWorld = isPublicWorld;
            _solutionDrivers = new DriversCollection();
            _gitRepositories = new HashSet<IGitRepository>();
            _userMonitorClient = userMonitorClient;

            CommandProviderName = "World";
            commandRegister.Register( this );
        }

        public NormalizedPath CommandProviderName { get; }

        bool IsInitialized => _localState != null;

        SolutionContext ISolutionDriverWorld.Register( ISolutionDriver driver )
        {
            var c = _solutionDrivers.Register( driver );
            if( _gitRepositories.Add( driver.GitRepository ) )
            {
                UpdateGlobalGitStatus();
            }
            return c.OnRegisterDriver( driver );
        }

        void ISolutionDriverWorld.Unregister( ISolutionDriver driver )
        {
            _solutionDrivers.Unregister( driver );

            _gitRepositories.Clear();
            _gitRepositories.AddRange( _solutionDrivers.AllDrivers.Select( d => d.GitRepository ) );
            UpdateGlobalGitStatus();
        }

        /// <summary>
        /// Gets whether this world is public.
        /// </summary>
        public bool IsPublicWorld { get; }

        /// <summary>
        /// Gets the world name.
        /// </summary>
        public IRootedWorldName WorldName { get; }

        /// <summary>
        /// Initializes this world state by loading the local and shared states.
        /// This initializes the user log level.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Initialize( IActivityMonitor monitor )
        {
            bool alreadyInitialized = _localState != null;
            if( !alreadyInitialized )
            {
                _localState = _store.GetOrCreateLocalState( monitor, WorldName );
                DoSetLogLevel( monitor, _localState.UserLogFilter, _localState.MonitorLogFilter, false );
                _sharedState = _store.GetOrCreateSharedState( monitor, WorldName );
            }
            return true;
        }

        /// <summary>
        /// Asks this world state to be dumped (in the monitor or anywhere else) by
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

        class OpenGraph : IDisposable
        {
            readonly StringBuilder _b;
            public OpenGraph( StringBuilder b ) => _b = b.AppendLine( " {" );
            public void Dispose() => _b.AppendLine( "}" );
        }


        static StringBuilder AppendGraphName( StringBuilder b, string name ) => b.Append( '"' ).Append( name ).Append( '"' );

        static StringBuilder AppendDependency( StringBuilder b, string dependent, string dependencies )
        {
            AppendGraphName( b, dependencies ).Append( " -> " );
            return AppendGraphName( b, dependent );
        }

        [CommandMethod]
        public bool DumpWorldGraph( IActivityMonitor m )
        {
            StringBuilder b = new StringBuilder();
            var ctx = _solutionDrivers.GetSolutionDependencyContextOnCurrentBranches( m );
            if( ctx == null ) return false;
            b.Append( "digraph " );
            AppendGraphName( b, WorldName.Name );
            using( new OpenGraph( b ) )
            {
                foreach( var slnGroupped in ctx.Solutions.GroupBy( p => p.Solution.Rank ) )
                {
                    b.Append( "subgraph " )
                        .Append( "rank_" )
                        .Append( slnGroupped.Key );
                    using( new OpenGraph( b ) )
                    {
                        b.Append( "rank=" )
                            .Append( slnGroupped.Key )
                            .AppendLine( ";" );
                        foreach( var sln in slnGroupped )
                        {

                            AppendGraphName( b, sln.Solution.Solution.Name );
                            b.AppendLine( ";" );
                            foreach( var slnDep in sln.Solution.PublishedRequirements )
                            {
                                AppendDependency( b, sln.Solution.Solution.Name, slnDep.Solution.Name );
                                b.AppendLine( ";" );
                            }

                            foreach( var slnBuildDep in sln.Solution.Requirements.Except( sln.Solution.PublishedRequirements ) )
                            {
                                AppendDependency( b, sln.Solution.Solution.Name, slnBuildDep.Solution.Name );
                                b.AppendLine( @" [style=""dotted""];" );
                            }
                        }
                    }
                }
            }
            File.WriteAllText( "graph.gv", b.ToString() );
            m.Info( "Generated graph.gv..." );
            return true;
        }

        [CommandMethod]
        public bool DumpWorldGraphWithProj( IActivityMonitor m )
        {
            StringBuilder b = new StringBuilder();
            var ctx = _solutionDrivers.GetSolutionDependencyContextOnCurrentBranches( m );
            if( ctx == null ) return false;
            b.Append( "digraph " );
            AppendGraphName( b, WorldName.Name );
            using( new OpenGraph( b ) )
            {
                b.AppendLine( "concentrate=true" )
                    .AppendLine( "compound=true" );

                int i = 0;
                foreach( var sln in ctx.Solutions )
                {
                    b.Append( "subgraph " )
                        .Append( "cluster_" )
                        .Append( i );
                    i++;
                    using( new OpenGraph( b ) )
                    {
                        b.Append( "label=" );
                        AppendGraphName( b, sln.Solution.Solution.Name )
                            .AppendLine( ";" )
                            .AppendLine( "style=filled;" )
                            .AppendLine( "node [style=filled];" )
                            .Append( "rank=" )
                            .Append( sln.Solution.Rank )
                            .AppendLine( ";" );
                        foreach( var proj in sln.Solution.Solution.Projects.Where( p => p.IsPublished ) )
                        {
                            AppendGraphName( b, proj.Name ).AppendLine( ";" );
                            foreach( var projDep in proj.ProjectReferences.Where( p => p.Target.IsPublished ) )
                            {
                                AppendDependency( b, proj.Name, projDep.Target.Name ).AppendLine( ";" );
                            }
                        }

                    }
                }

                foreach( var dep in ctx.DependencyContext.PackageDependencies )
                {
                    if( !dep.OriginIsPublishedProject ) continue;
                    AppendDependency( b, dep.RefererOrigin.Name, dep.TargetProject.Name ).AppendLine( ";" );
                }
            }
            File.WriteAllText( "graph.gv", b.ToString() );
            m.Info( "Generated graph.gv..." );
            return true;
        }

        /// <summary>
        /// Gets the shared state.
        /// </summary>
        public SharedWorldState SharedWorldState => _sharedState;

        /// <summary>
        /// Gets the local state.
        /// </summary>
        public LocalWorldState LocalWorldState => _localState;

        /// <summary>
        /// Raised by <see cref="DumpWorldState(IActivityMonitor)"/>.
        /// </summary>
        public event EventHandler<EventMonitoredArgs> DumpWorldStatus;

        /// <summary>
        /// Gets the registered solution drivers.
        /// </summary>
        public DriversCollection SolutionDrivers => _solutionDrivers;

        /// <summary>
        /// Gets the set of <see cref="IGitRepository"/> that has been discovered
        /// (thanks to the registration of at one <see cref="ISolutionDriver"/> on a branch).
        /// </summary>
        public IReadOnlyCollection<IGitRepository> GitRepositories => _gitRepositories;


        /// <summary>
        /// Secure the execution of the lambda in a try/catch.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="message"></param>
        /// <param name="action"></param>
        /// <returns></returns>
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
        /// Gets whether the CODECAKEBUILDER_SECRET_KEY is known to the user.
        /// </summary>
        public bool CanCICDKeyVaultUpdate => _keyStore.IsSecretKeyAvailable( "CODECAKEBUILDER_SECRET_KEY" ) == true;

        /// <summary>
        /// Updates a secret in the <see cref="SharedWorldState.CICDKeyVault"/>.
        /// When <paramref name="secret"/> is null, it is removed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The secret name. Must not be null or white space.</param>
        /// <param name="secret">The secret. Null or empty to remove it.</param>
        [CommandMethod]
        public void CICDKeyVaultUpdate( IActivityMonitor m, string name, string secret )
        {
            if( !CanCICDKeyVaultUpdate ) throw new InvalidOperationException( nameof( CanCICDKeyVaultUpdate ) );
            if( String.IsNullOrWhiteSpace( name ) ) throw new ArgumentNullException( nameof( name ) );

            var passPhrase = _keyStore.GetSecretKey( m, "CODECAKEBUILDER_SECRET_KEY", true );
            var vault = KeyVault.DecryptValues( _sharedState.CICDKeyVault, passPhrase );

            bool exists = vault.ContainsKey( name );
            if( String.IsNullOrEmpty( secret ) )
            {
                if( exists )
                {
                    m.Info( $"Removing secret '{name}' from CICDKeyVault." );
                    vault.Remove( name );
                }
                else
                {
                    m.Warn( $"Secret '{name}' not found in CICDKeyVault." );
                }
            }
            else
            {
                if( exists ) m.Info( $"Updating secret '{name}' in CICDKeyVault." );
                else m.Info( $"Adding new secret '{name}' in CICDKeyVault." );
                vault[name] = secret;
            }
            _sharedState.CICDKeyVault = KeyVault.EncryptValuesToString( vault, passPhrase );
            _sharedState.SaveState( m );
        }


        /// <summary>
        /// Calls <see cref="UpdateGlobalGitStatus"/> and checks the current Git status.
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
        /// Calls <see cref="UpdateGlobalGitStatus"/> and checks the current Git status.
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
        public GlobalWorkStatus? WorkStatus => _localState?.WorkStatus;

        /// <summary>
        /// Sets the <see cref="WorkStatus"/>.
        /// </summary>
        /// <param name="s">The work status.</param>
        bool SetWorkStatusAndSave( IActivityMonitor m, GlobalWorkStatus s )
        {
            if( WorkStatus != s ) _localState.WorkStatus = s;
            return _localState.SaveState( m );
        }

        [CommandMethod( confirmationRequired: false )]
        public void SetLogLevel( IActivityMonitor m, LogFilter userLevel, LogFilter monitorLevel ) => DoSetLogLevel( m, userLevel, monitorLevel, true );

        void DoSetLogLevel( IActivityMonitor m, LogFilter userLevel, LogFilter monitorLevel, bool saveOnChange )
        {
            if( _userMonitorClient.MinimalFilter != userLevel )
            {
                _userMonitorClient.MinimalFilter = userLevel;
                _localState.UserLogFilter = userLevel;
            }
            if( m.MinimalFilter != monitorLevel )
            {
                m.MinimalFilter = monitorLevel;
                _localState.MonitorLogFilter = monitorLevel;
            }
            var msg = $"Log levels: UserLevel = '{userLevel}', MonitorLevel = {monitorLevel}.";
            Console.WriteLine( msg );
            m.UnfilteredLog( ActivityMonitor.Tags.Empty, LogLevel.Info, msg, m.NextLogTime(), null );
            if( saveOnChange ) _localState.SaveState( m );
        }

        string? GetCleanBranchName( IActivityMonitor monitor )
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
        IWorldSolutionContext? GetWorldSolutionContext( IActivityMonitor monitor, bool reloadSolutions = false )
        {
            var branchName = GetCleanBranchName( monitor );
            if( branchName == null ) return null;
            var c = _solutionDrivers.GetContextOnBranch( branchName )
                ?? throw new Exception( $"No solution context available for branch {branchName}. GitBranchPlugins are not initialized or a ISolutionDriver plugin implementation is missing." );
            return c.Refresh( monitor, reloadSolutions );
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
                    if( !g.SwitchDevelopToLocal( m, autoCommit: true ) )
                    {
                        UpdateGlobalGitStatus();
                        return;
                    }
                }
                UpdateGlobalGitStatus();
                Debug.Assert( CachedGlobalGitStatus == StandardGitStatus.Local );

                var depContext = GetWorldSolutionContext( m );
                if( depContext == null ) return;

                if( ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext ) == null ) return;

                if( !error() )
                {
                    SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
                }
            } );
        }

        IReadOnlyList<PackageReference> GetExternalPackageReferences( IActivityMonitor m, IWorldSolutionContext ctx )
        {
            var externals = ctx.DependencyContext.Analyzer.ExternalReferences;
            if( externals.Count == 0 )
            {
                m.Warn( "This World doesn't have any external references." );
            }
            else
            {
                using( m.OpenInfo( $"Refreshing external versions for {externals.Count} packages." ) )
                {
                    foreach( var e in externals )
                    {
                        _artifacts.GetExternalVersions( m, e.Target.Artifact );
                    }
                }
            }
            return externals;
        }

        [CommandMethod]
        public void ShowExternalDependencies( IActivityMonitor m, bool compact = true, bool onlyMultipleVersions = false, PackageQuality quality = PackageQuality.None )
        {
            var ctx = _solutionDrivers.GetSolutionDependencyContextOnCurrentBranches( m );
            if( ctx == null ) return;
            var externals = GetExternalPackageReferences( m, ctx );
            ConsoleColor stdForeColor = Console.ForegroundColor;
            ConsoleColor stdBackColor = Console.BackgroundColor;
            foreach( var byType in externals.GroupBy( g => g.Target.Artifact.Type ).OrderBy( g => g.Key.Name ) )
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.BackgroundColor = ConsoleColor.White;
                Console.WriteLine( $"{byType.Key} external dependencies:" );
                Console.ForegroundColor = stdForeColor;
                Console.BackgroundColor = stdBackColor;
                foreach( var byName in byType.GroupBy( g => g.Target.Artifact ).OrderBy( g => g.Key.Name ) )
                {
                    var byVersion = byName.GroupBy( s => s.Target.Version ).ToList();
                    if( !onlyMultipleVersions || byVersion.Count() > 1 )
                    {
                        var maxVersion = byVersion.Select( v => v.Key ).Max();
                        var externalVersionDisplay = _artifacts.GetExternalVersions( m, byName.Key )
                                                               .SelectMany( a => a.Versions.Where( v => v.PackageQuality >= quality && v > maxVersion ).Select( v => (v, a.FeedName) ) )
                                                               .GroupBy( v => v.v )
                                                               .OrderByDescending( v => v.Key )
                                                               .Select( g => $"{g.Key} ({g.Select( vn => vn.FeedName ).Concatenate()})" )
                                                               .Concatenate();

                        if( externalVersionDisplay.Length > 0 ) externalVersionDisplay = " <= " + externalVersionDisplay;
                        Console.Write( $"    |" );
                        Console.Write( byName.Key.Name );
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine( externalVersionDisplay );
                        Console.ForegroundColor = stdForeColor;
                        if( byVersion.Count() > 1 ) Console.ForegroundColor = ConsoleColor.DarkYellow;
                        if( compact )
                        {
                            foreach( var v in byVersion )
                            {
                                Console.WriteLine( $"    |      => {v.Key} ({v.GroupBy( p => p.Referer.Solution ).Select( s => $"{s.Key.Name}" ).Concatenate()})" );
                            }
                        }
                        else
                        {
                            foreach( var versionGrouped in byVersion )
                            {
                                Console.WriteLine( "    |    |" + versionGrouped.Key );
                                foreach( var solutionGrouped in versionGrouped.GroupBy( q => q.Referer.Solution ) )
                                {
                                    Console.WriteLine( "    |    |    |" + solutionGrouped.Key.Name + ":" );
                                    foreach( var project in solutionGrouped )
                                    {
                                        Console.WriteLine( "    |    |    |    |" + project.Referer.Name );
                                    }
                                }
                            }
                        }
                        Console.ForegroundColor = stdForeColor;
                    }
                }
            }
        }

        [CommandMethod]
        public void UpgradeDependency( IActivityMonitor m, string packageName, string? versionToUpgrade = null )
        {
            var worldCtx = _solutionDrivers.GetSolutionDependencyContextOnCurrentBranches( m );
            if( worldCtx == null ) return;
            SVersion version;
            var externalReferences = worldCtx.DependencyContext.Analyzer.ExternalReferences;
            List<PackageReference> artifactUses = externalReferences
                    .Where( p =>
                        p.Target.Artifact.TypedName.Equals( packageName, StringComparison.OrdinalIgnoreCase )
                        || p.Target.Artifact.Name.Equals( packageName, StringComparison.OrdinalIgnoreCase ) )
                    .ToList();
            if( artifactUses.Count == 0 )
            {
                m.Error( $"No solution contain the package {packageName}." );
                return;
            }
            var types = new HashSet<ArtifactType>( artifactUses.Select( p => p.Target.Artifact.Type! ) );
            if( types.Count > 1 )
            {
                m.Error( $"Ambiguous package name '{packageName}', use its TypedName to disambiguate: {types.Select( t => t.Name + ':' + packageName ).Concatenate( " or " )}." );
                return;
            }
            versionToUpgrade = versionToUpgrade?.Trim();
            if( String.IsNullOrEmpty( versionToUpgrade ) )
            {
                version = artifactUses.Max( pckg => pckg.Target.Version );
                m.Info( $"No target version provided: automatically using the maximal {version} currently referenced." );
            }
            else
            {
                if( !SVersion.TryParse( versionToUpgrade, out version ) )
                {
                    m.Fatal( $"Unable to parse target version '{versionToUpgrade}' string." );
                    return;
                }
            }
            var artifactToUpgrade = artifactUses.Where( p => p.Target.Version != version ).ToList();
            if( artifactToUpgrade.Count == 0 )
            {
                m.Info( $"No package to upgrade: {artifactUses.Count} references to {packageName} already use version {version}." );
                return;
            }
            using( m.OpenInfo( $"{artifactToUpgrade.Count} packages to upgrade (out of {artifactUses.Count} package references)." ) )
            {
                foreach( var slnGroupedPackage in artifactToUpgrade.GroupBy( s => s.Referer.Solution ) )
                {
                    var sln = worldCtx.Solutions.First( s => s.Solution.Solution == slnGroupedPackage.Key );
                    sln.Driver.UpdatePackageDependencies( m,
                        slnGroupedPackage.Select(
                            p => new UpdatePackageInfo( p.Referer, new ArtifactInstance( p.Target.Artifact, version ) )
                        ).ToList()
                    );
                }
            }
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
                var depContext = GetWorldSolutionContext( m );
                if( depContext == null ) return;
                ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext );
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
            monitor.MinimalFilter = LogFilter.Trace;
            return RunSafe( monitor, $"Local build.", ( m, error ) =>
            {
                var ctx = GetWorldSolutionContext( m );
                if( ctx == null ) return;

                ZeroBuilder zBuilder = ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, ctx );
                if( zBuilder == null ) return;

                ctx = GetWorldSolutionContext( m );
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
            _localState.SetBuildResult( result );
            return _localState.SaveState( m );
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
                var depContext = GetWorldSolutionContext( m );
                if( depContext == null ) return;

                var zBuilder = ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext );
                if( zBuilder == null ) return;

                depContext = GetWorldSolutionContext( m );
                if( depContext == null ) return;

                var builder = new DevelopBuilder( zBuilder, _artifacts, _localFeedProvider, depContext, false );
                if( !RunBuild( m, builder, false ) ) return;

                if( !error() )
                {
                    SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
                }
            } );
        }

        ReleaseRoadmap? LoadRoadmap( IActivityMonitor monitor )
        {
            var depContext = GetWorldSolutionContext( monitor );
            if( depContext == null ) return null;
            return ReleaseRoadmap.Create( monitor, depContext, _localState.Roadmap );
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
        public bool EditRoadmap( IActivityMonitor monitor, bool pull = true )
        {
            if( !CanEditRoadmap ) throw new InvalidOperationException( nameof( CanEditRoadmap ) );
            if( !CheckBeforeReleaseBuildOrEdit( monitor, pull ) ) return false;
            return DoEditRoadmap( monitor, false ) != null;
        }

        ReleaseRoadmap? DoEditRoadmap( IActivityMonitor monitor, bool forgetAllExistingRoadmapVersions )
        {
            var roadmap = LoadRoadmap( monitor );
            if( roadmap == null ) return null;
            bool editSucceed = roadmap.UpdateRoadmap( monitor, VersionSelector, forgetAllExistingRoadmapVersions );
            // Always saves state to preserve any change in the roadmap, even on error.
            _localState.Roadmap = roadmap.ToXml();
            if( !_localState.SaveState( monitor ) || !editSucceed ) return null;
            return roadmap;
        }

        /// <summary>
        /// Gets whether <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/>, <see cref="VersionSelector"/>
        /// and <see cref="CachedGlobalGitStatus"/> is on <see cref="StandardGitStatus.Develop"/>.
        /// </summary>
        public bool CanRelease => VersionSelector != null
                                  && WorkStatus == GlobalWorkStatus.Idle
                                  && CachedGlobalGitStatus == StandardGitStatus.Develop;

        public bool CheckBeforeReleaseBuildOrEdit( IActivityMonitor m, bool pull )
        {
            if( !CheckGlobalGitStatus( m, StandardGitStatus.Develop ) ) return false;
            bool reloadNeeded = false;
            foreach( var g in _gitRepositories )
            {
                if( !g.CheckCleanCommit( m ) ) return false;
                if( pull )
                {
                    // We first Checkout and pulls the Master branch
                    // Ensuring a successful CheckOut of Master is a welcome security.
                    g.EnsureBranch( m, WorldName.MasterBranchName );
                    if( !g.Checkout( m, WorldName.MasterBranchName ).Success ) return false;
                    // Checking out the Develop branch back.
                    var (Success, ReloadNeeded) = g.Checkout( m, WorldName.DevelopBranchName, skipFetchBranches: true );
                    if( !Success ) return false;
                    reloadNeeded |= ReloadNeeded;

                }
            }
            if( reloadNeeded && GetWorldSolutionContext( m, true ) == null ) return false;
            return true;
        }

        /// <summary>
        /// Starts a release after an optional pull, using the current <see cref="VersionSelector"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="pull">Pull all branches first.</param>
        /// <param name="resetRoadmap">True to forget the current road-map (it if exists) and ask for each and every version.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Release( IActivityMonitor monitor, bool pull = true, bool resetRoadmap = false )
        {
            if( !CanRelease ) throw new InvalidOperationException( nameof( CanRelease ) );
            if( !CheckBeforeReleaseBuildOrEdit( monitor, pull ) ) return false;

            var roadmap = DoEditRoadmap( monitor, resetRoadmap );
            if( roadmap == null ) return false;

            if( !SetWorkStatusAndSave( monitor, GlobalWorkStatus.Releasing ) ) return false;

            return DoReleasing( monitor );
        }

        bool DoReleasing( IActivityMonitor monitor )
        {
            return RunSafe( monitor, $"Starting Release.", ( m, error ) =>
            {
                bool firstRun = _localState.GetGitSnapshot() == null;
                if( firstRun )
                {
                    using( m.OpenInfo( $"First run: capturing {WorldName.DevelopBranchName} and {WorldName.MasterBranchName} branches positions." ) )
                    {
                        var snapshot = new XElement( XmlNames.xGitSnapshot,
                                                 _gitRepositories.Select( g => new XElement( XmlNames.xG,
                                                            new XAttribute( XmlNames.xP, g.SubPath ),
                                                            new XAttribute( XmlNames.xD, g.GetBranchSha( m, WorldName.DevelopBranchName ) ),
                                                            new XAttribute( XmlNames.xM, g.GetBranchSha( m, WorldName.MasterBranchName ) ?? "" ) ) ) );
                        _localState.SetGitSnapshot( snapshot );
                        if( !_localState.SaveState( m ) ) return;
                        if( !_localFeedProvider.Release.RemoveAll( m ) ) return;
                    }
                }

                var depContext = GetWorldSolutionContext( m );
                if( depContext == null ) return;

                ZeroBuilder? zBuilder = ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, depContext );
                if( zBuilder == null ) return;

                var roadmap = LoadRoadmap( monitor );
                if( roadmap == null || !roadmap.IsValid )
                {
                    monitor.Error( $"Road map is invalid. Current release should be canceled." );
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
            return RunSafe( monitor, $"Canceling current Release.", ( m, error ) =>
            {
                if( !HandleReleaseVersionTags( m, false ) ) return;
                var snapshot = _localState.GetGitSnapshot();
                if( snapshot != null )
                {
                    using( m.OpenInfo( $"Restoring '{WorldName.DevelopBranchName}' and '{WorldName.MasterBranchName}' branches positions." ) )
                    {
                        foreach( var e in snapshot.Elements() )
                        {
                            var path = (string)e.AttributeRequired( XmlNames.xP );
                            var git = _gitRepositories.FirstOrDefault( g => g.SubPath == path );
                            if( git == null )
                            {
                                m.Error( $"Unable to find Git repository for {path}." );
                                return;
                            }
                            if( !git.ResetBranchState( m, WorldName.MasterBranchName, (string)e.AttributeRequired( XmlNames.xM ) )
                                || !git.ResetBranchState( m, WorldName.DevelopBranchName, (string)e.AttributeRequired( XmlNames.xD ) ) )
                            {
                                return;
                            }
                        }
                    }
                    _localState.ClearGitSnapshot();
                }
                GetWorldSolutionContext( m, true );
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
                var versions = ReleaseRoadmap.Load( _localState.Roadmap )
                                             .Select( e => (e.SubPath, e.Info, Git: _gitRepositories.FirstOrDefault( g => e.SubPath.StartsWith( g.SubPath ) )) );
                foreach( var (SubPath, Info, Git) in versions )
                {
                    if( Git == null )
                    {
                        m.Fatal( $"Unable to find Git repository for {SubPath} from current Road-map." );
                        return false;
                    }
                    if( pushVersionTagsAndBranches )
                    {
                        if( Info.Level != ReleaseLevel.None )
                        {
                            success &= Git.PushVersionTag( m, Info.Version );
                            // Since we have a version: we may be on master.
                            if( success && Info.Version.PackageQuality == PackageQuality.Stable )
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
                                    && _localState.GetBuildResult( BuildResultType.CI ) != null
                                    && ((int)_cachedGlobalGitStatus & (int)StandardGitStatus.MasterOrDevelop) > 0;

        [CommandMethod]
        public bool PublishCI( IActivityMonitor monitor )
        {
            if( !CanPublishCI ) throw new InvalidOperationException( nameof( CanPublishCI ) );
            return RunSafe( monitor, $"Publishing CI.", ( m, error ) =>
            {
                var buildResults = _localState.GetBuildResult( BuildResultType.CI );
                if( !DoPushArtifacts( m, buildResults ) ) return;
                bool success = true;
                foreach( var g in _gitRepositories )
                {
                    success &= g.Push( m, WorldName.DevelopBranchName );
                }
                if( !success ) return;
                _localState.PublishBuildResult( buildResults.Type );
                _localState.SaveState( m );
            } );
        }

        bool DoPublishingRelease( IActivityMonitor monitor )
        {
            return RunSafe( monitor, $"Publishing Release.", ( m, error ) =>
            {
                var buildResults = _localState.GetBuildResult( BuildResultType.Release );
                if( buildResults == null || !DoPushArtifacts( m, buildResults ) ) return;
                if( !HandleReleaseVersionTags( m, true ) ) return;
                _localState.PublishBuildResult( buildResults.Type );
                if( !error() )
                {
                    if( _localState.GetGitSnapshot() != null ) _localState.ClearGitSnapshot();
                    SetWorkStatusAndSave( m, GlobalWorkStatus.Idle );
                }
            } );
        }

        bool DoPushArtifacts( IActivityMonitor monitor, BuildResult buildResults )
        {
            Debug.Assert( buildResults.Type == BuildResultType.Release || buildResults.Type == BuildResultType.CI );
            IEnvLocalFeed local = _localFeedProvider.GetFeed( buildResults.Type );
            return RunSafe( monitor, $"Publishing Artifacts from local '{local.PhysicalPath}'.", ( m, error ) =>
            {
                var byTargetRepository = buildResults.GeneratedArtifacts.GroupBy( a => a.TargetName );
                foreach( var a in byTargetRepository )
                {
                    using( m.OpenInfo( $"Publishing to target: {a.Key}." ) )
                    {
                        IArtifactRepository h = _artifacts.Repositories.SingleOrDefault( repo => repo.UniqueRepositoryName == a.Key );
                        if( !local.PushLocalArtifacts( m, h, a.Select( p => p.Artifact ), IsPublicWorld ) )
                        {
                            Debug.Assert( error(), "An error must have been logged." );
                            m.Warn( "Continuing push process despite the error to maximize the number of pushed artifacts." );
                        }
                    }
                }
            } );
        }

        public bool CanCreateLTS => WorldName.ParallelName == null
                                    && WorkStatus == GlobalWorkStatus.Idle
                                    && CachedGlobalGitStatus == StandardGitStatus.Develop;

        [CommandMethod( confirmationRequired: true )]
        public bool CreateLTS( IActivityMonitor m, string parallelName )
        {
            if( !CanCreateLTS ) throw new InvalidOperationException( nameof( CanCreateLTS ) );
            if( String.IsNullOrWhiteSpace( parallelName ) ) throw new ArgumentException( "Must not be null or white space.", nameof( parallelName ) );

            if( !CheckGlobalGitStatus( m, StandardGitStatus.Develop ) ) return false;

            using( m.OpenInfo( "Checking working folders and pulling remote." ) )
            {
                foreach( var repo in _gitRepositories )
                {
                    if( !repo.CheckCleanCommit( m ) )
                    {
                        m.Error( "Working folder must be clean. Aborting." );
                        return false;
                    }
                    if( !repo.Checkout( m, repo.CurrentBranchName ).Success )
                    {
                        m.Error( "Failed to pull current branch. Aborting." );
                        return false;
                    }
                }
            }

            var worldData = _store.ReadWorldDescription( m, WorldName );
            var worldRoot = worldData.Root;
            var newWorldName = new WorldName( WorldName.Name, parallelName );

            var toProcess = new List<(IGitRepository Repo, SimpleGitVersion.ICommitInfo Current)>();

            worldRoot.Name = $"{newWorldName.Name}-{newWorldName.ParallelName}.World";
            using( m.OpenInfo( $"Changing the xml world definition: root element name becomes '{worldRoot.Name}'. Changing Branch elements Name from '{WorldName.DevelopBranchName}' to '{newWorldName.DevelopBranchName}' (or '{WorldName.MasterBranchName}' to '{newWorldName.MasterBranchName}')." ) )
            {
                foreach( XElement xBranch in worldData.Root.Descendants( "Branch" ) )
                {
                    var oldBranch = xBranch.AttributeRequired( "Name" );
                    string oldBranchName = (string)oldBranch;
                    Uri uri = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( new Uri( (string)xBranch.Parent.AttributeRequired( "Url" ) ) );

                    if( oldBranchName == WorldName.MasterBranchName || oldBranchName == WorldName.DevelopBranchName )
                    {
                        var branchName = oldBranchName == WorldName.MasterBranchName ? newWorldName.MasterBranchName : newWorldName.DevelopBranchName;
                        var repo = _gitRepositories.Single( p =>  uri == p.OriginUrl );
                        var commitInfo = repo.ReadVersionInfo( m );
                        if( commitInfo == null )
                        {
                            m.Error( $"Unable to read version on '{repo.SubPath}'. Aborting." );
                            return false;
                        }
                        toProcess.Add( (repo, commitInfo) );
                        oldBranch.Value = branchName;
                    }
                    else
                    {
                        m.Error( $"Branch for '{uri}': Name is '{oldBranchName}', it should be '{WorldName.MasterBranchName}' nor '{newWorldName.DevelopBranchName}'. Aborting" );
                        return false;
                    }
                }
            }
            bool error = false;
            using( m.OpenInfo( $"Processing changes (updating Repository.xml in both branches)." ) )
            {
                foreach( var p in toProcess )
                {
                    try
                    {
                        // Creating the parallel world develop branch and checking it out.
                        p.Repo.EnsureBranch( m, newWorldName.DevelopBranchName, noWarnOnCreate: true );
                        if( p.Repo.Checkout( m, newWorldName.DevelopBranchName, skipFetchBranches: true, skipPullMerge: true ).Success )
                        {
                            int currentMajor = p.Current.FinalBuildInfo.Version.Major;
                            // On the parallel develop branch, sets the SingleMajor to the current
                            // one or sets the OnlyPatch to true if it is the 0 major... and commit.
                            var repoXmlFile = p.Repo.FullPhysicalPath.AppendPart( "RepositoryInfo.xml" );
                            var repoXmlDoc = XDocument.Load( repoXmlFile );
                            string commitMessage;
                            if( currentMajor == 0 )
                            {
                                repoXmlDoc.Root.EnsureElement( SimpleGitVersion.SGVSchema.SimpleGitVersion )
                                                .SetAttributeValue( SimpleGitVersion.SGVSchema.OnlyPatch, true );
                                commitMessage = $"Creating parallel world '{newWorldName}': Version Major is locked to {currentMajor}.";
                            }
                            else
                            {
                                repoXmlDoc.Root.EnsureElement( SimpleGitVersion.SGVSchema.SimpleGitVersion )
                                                .SetAttributeValue( SimpleGitVersion.SGVSchema.SingleMajor, currentMajor );
                                commitMessage = $"Creating parallel world '{newWorldName}': Only patch versions {currentMajor}.{p.Current.FinalBuildInfo.Version.Minor}.X where X > {p.Current.FinalBuildInfo.Version.Patch} can be produced.";
                            }
                            repoXmlDoc.Save( repoXmlFile );
                            if( !p.Repo.Commit( m, commitMessage ) )
                            {
                                error = true;
                            }
                            m.Info( $"SingleMajor for {p.Repo.SubPath}: '{commitMessage}'." );
                        }
                        else
                        {
                            error = true;
                        }
                        // Back to the current default world branch (even if error is true).
                        if( !p.Repo.Checkout( m, WorldName.DevelopBranchName, skipFetchBranches: true, skipPullMerge: true ).Success )
                        {
                            // This is bad...
                            error = true;
                        }
                        else
                        {
                            // On the current develop branch (the default world), sets the StartingVersion to the (Major+1).0.0-alpha 
                            // or, if the Major is 0 to the 0.(Minor+1).0-alpha.
                            int currentMajor = p.Current.FinalBuildInfo.Version.Major;
                            var starting = currentMajor > 0 ? $"{currentMajor + 1}.0.0-alpha" : $"{currentMajor}.{p.Current.FinalBuildInfo.Version.Minor + 1}.0-alpha";
                            var repoXmlFile = p.Repo.FullPhysicalPath.AppendPart( "RepositoryInfo.xml" );
                            var repoXmlDoc = XDocument.Load( repoXmlFile );
                            repoXmlDoc.Root.EnsureElement( SimpleGitVersion.SGVSchema.SimpleGitVersion )
                                            .SetAttributeValue( SimpleGitVersion.SGVSchema.StartingVersion, starting );
                            repoXmlDoc.Save( repoXmlFile );
                            if( !p.Repo.Commit( m, $"Creating parallel world '{newWorldName}': minimal version in this branch is now '{starting}'." ) )
                            {
                                error = true;
                            }
                            m.Info( $"StartingVersion for {p.Repo.SubPath} '{WorldName.DevelopBranchName}' is now '{starting}'." );
                        }
                    }
                    catch( Exception ex )
                    {
                        m.Error( "Unexpected error.", ex );
                        error = true;
                    }
                    if( error ) break;
                }
            }
            if( !error )
            {
                error = _store.CreateNewParrallel( m, WorldName, parallelName, worldData ) == null;
            }
            if( error )
            {
                using( m.OpenInfo( "An error occurred. Attempting to restore the repositories." ) )
                {
                    foreach( var p in toProcess )
                    {
                        p.Repo.ResetBranchState( m, newWorldName.DevelopBranchName, string.Empty );
                        p.Repo.ResetBranchState( m, WorldName.DevelopBranchName, p.Current.FinalBuildInfo.CommitSha );
                    }
                }
            }
            else
            {
                using( m.OpenInfo( $"Parallel world created. Pushing '{newWorldName.DevelopBranchName}' and '{WorldName.DevelopBranchName}' branches to remote." ) )
                {
                    foreach( var p in toProcess )
                    {
                        p.Repo.Push( m, newWorldName.DevelopBranchName );
                        p.Repo.Push( m, WorldName.DevelopBranchName );
                    }
                }
            }
            return error;
        }

    }

}
