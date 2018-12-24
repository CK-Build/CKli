using CK.Core;
using CK.Env;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CKli
{
    public class XSolutionCentral : XTypedObject, IDependentSolutionContextLoader, ICommandMethodsProvider
    {
        readonly IWorldName _world;
        readonly IWorldStore _worldStore;
        readonly XLocalFeedProvider _packageFeeds;
        readonly MSBuildContext _msBuildContext;
        readonly WorldState _worldState;
        readonly XSecretKeyStore _publishKeyStore;
        readonly CommandRegister _commandRegister;
        readonly List<XGitFolder> _allGitFolders;

        readonly List<XSolutionBase> _allSolutions;
        readonly List<XSolutionBase> _allDevelopSolutions;
        readonly List<XGitFolder> _allGitFoldersWithDevelopBranchName;

        public XSolutionCentral(
            FileSystem fileSystem,
            IWorldName world,
            IWorldStore worldStore,
            XSecretKeyStore publishKeyStore,
            XLocalFeedProvider packageFeeds,
            XArtifactCenter artifacts,
            CommandRegister commandRegister,
            Initializer initializer )
            : base( initializer )
        {
            _world = world;
            _worldStore = worldStore;
            _packageFeeds = packageFeeds;
            _publishKeyStore = publishKeyStore;
            _commandRegister = commandRegister;

            _msBuildContext = new MSBuildContext( fileSystem );
            fileSystem.ServiceContainer.Add( _msBuildContext );

            _allGitFolders = new List<XGitFolder>();
            initializer.Services.Add( this );

            _allSolutions = new List<XSolutionBase>();
            _allDevelopSolutions = new List<XSolutionBase>();
            _allGitFoldersWithDevelopBranchName = new List<XGitFolder>();

            _worldState = new WorldState( commandRegister, artifacts.ArtifactCenter, worldStore, world, this, _packageFeeds );
            _worldState.VersionSelector = new ReleaseVersionSelector();
            _worldState.Initializing += ( o, e ) => DumpGitFolderStatus( e.Monitor );
            fileSystem.ServiceContainer.Add( _worldState );
            fileSystem.ServiceContainer.Add<ISolutionDriverWorld>( _worldState );

            // Extends the 'World' commands.
            CommandProviderName = _worldState.CommandProviderName;
            commandRegister.Register( this );
        }

        public NormalizedPath CommandProviderName { get; }

        internal void Register( XGitFolder g )
        {
            _allGitFolders.Add( g );
        }

        protected override bool OnSiblingsCreated( IActivityMonitor m )
        {
            foreach( var g in _allGitFolders )
            {
                bool hasDevelop = false;
                foreach( var b in g.Branches )
                {
                    if( b.Solutions.Count > 0 )
                    {
                        bool isDevelop = b.Name == _world.DevelopBranchName;
                        foreach( var s in b.Solutions )
                        {
                            _allSolutions.Add( s );
                            if( isDevelop )
                            {
                                _allDevelopSolutions.Add( s );
                                hasDevelop = true;
                            }
                        }
                    }
                }
                if( hasDevelop ) _allGitFoldersWithDevelopBranchName.Add( g );
            }
            return base.OnSiblingsCreated( m );
        }

        /// <summary>
        /// Gets the current world.
        /// </summary>
        public IWorldName World => _world;

        [CommandMethod]
        public void DumpGitFolderStatus( IActivityMonitor m )
        {
            var gitFolders = _allGitFolders.Select( x => x.GitFolder );
            int gitFoldersCount = 0;
            bool hasPluginInitError = false;
            var dirty = new List<string>();
            foreach( var git in gitFolders )
            {
                ++gitFoldersCount;
                using( m.OpenInfo( $"{git.SubPath} - branch: {git.CurrentBranchName}." ) )
                {
                    string pluginInfo;
                    if( !git.EnsureCurrentBranchPlugins( m ) )
                    {
                        hasPluginInitError = true;
                        pluginInfo = "Plugin initialization error.";
                    }
                    else pluginInfo = $"({git.PluginManager.BranchPlugins[git.CurrentBranchName].Count} plugins)";
                    if( git.CheckCleanCommit( m ) ) m.CloseGroup( "Up-to-date. " + pluginInfo );
                    else
                    {
                        dirty.Add( git.SubPath );
                        m.CloseGroup( "Dirty. " + pluginInfo );
                    }
                }
            }
            m.CloseGroup( $"{dirty.Count} dirty (out of {gitFoldersCount})." );
            if( dirty.Count > 0 ) m.Info( $"Dirty: {dirty.Concatenate()}" );
            var byActiveBranch = gitFolders.GroupBy( g => g.CurrentBranchName );
            if( byActiveBranch.Count() > 1 )
            {
                using( m.OpenInfo( $"{byActiveBranch.Count()} different branches:" ) )
                {
                    foreach( var b in byActiveBranch )
                    {
                        m.Info( $"Branch '{b.Key}': {b.Select( g => g.SubPath.Path ).Concatenate()}" );
                    }
                }
            }
            else m.Info( $"All {gitFoldersCount} git folders are on '{byActiveBranch.First().Key}' branch." );
            if( hasPluginInitError )
            {
                m.Error( "At least one git folder is unable to initialize its plugins." );
            }
        }

        /// <summary>
        /// Gets the MSBuild context that handles solutions and projects files.
        /// </summary>
        public MSBuildContext MSBuildContext => _msBuildContext;

        /// <summary>
        /// Gets all Git folders that have a <see cref="IWorldName.DevelopBranchName"/>
        /// and at least one solution in it in the order of their definition in the World xml file.
        /// </summary>
        public IReadOnlyCollection<XGitFolder> AllGitFoldersWithDevelopBranchName => _allGitFoldersWithDevelopBranchName;

        /// <summary>
        /// Gets all the solutions regardless of their type or branch in the order of their definition in
        /// the World xml file.
        /// </summary>
        public IReadOnlyList<XSolutionBase> AllSolutions => _allSolutions;

        /// <summary>
        /// Gets all the solutions regardless of their type in branch <see cref="World"/>.<see cref="GlobalContext.World.DevelopBranchName">DevelopBranchName</see>.
        /// The order is important here (order of their definition in the World xml file): Primary solutions necessarily
        /// appear before any of their secondary solutions (this is to avoid loading twice the secondary solutions).
        /// </summary>
        public IReadOnlyList<XSolutionBase> AllDevelopSolutions => _allDevelopSolutions;

        bool IDependentSolutionContextLoader.ReloadSolutions( IActivityMonitor m, IEnumerable<IGitRepository> repositories, string branchName )
        {
            using( m.OpenInfo( $"Reloading solutions for branch {branchName}." ) )
            {
                m.MinimalFilter = LogFilter.Terse;
                IReadOnlyList<Solution> solutions = GetAllSolutions( m, repositories, true, branchName );
                return solutions.All( s => s != null );
            }
        }

        IDependentSolutionContext IDependentSolutionContextLoader.Load( IActivityMonitor m, IEnumerable<IGitRepository> repositories, string branchName, bool forceReload )
        {
            using( m.OpenInfo( $"Computing SolutionDependencyContext for branch {branchName}." ) )
            {
                m.MinimalFilter = LogFilter.Terse;
                IReadOnlyList<Solution> solutions = GetAllSolutions( m, repositories, forceReload, branchName );
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

        IReadOnlyList<Solution> GetAllSolutions( IActivityMonitor m, IEnumerable<IGitRepository> repositories, bool forceReload, string branchName )
        {
            if( branchName == _world.DevelopBranchName )
            {
                return AllDevelopSolutions.Select( s => s.GetSolution( m, forceReload, branchName ) ).ToList();
            }
            var solutions = new List<Solution>();
            foreach( var g in _allGitFolders )
            {
                if( repositories.Contains( g.GitFolder ) )
                {
                    var b = g.Branches.FirstOrDefault( x => x.Name == branchName ) ?? g.DevelopBranch;
                    if( b != null )
                    {
                        foreach( var s in b.Solutions )
                        {
                            solutions.Add( s.GetSolution( m, forceReload, branchName ) );
                        }
                    }
                }
            }
            return solutions;
        }


    }
}
