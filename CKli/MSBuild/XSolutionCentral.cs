using CK.Core;
using CK.Env;
using CK.Env.MSBuild;
using CK.Text;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CKli
{
    public class XSolutionCentral : XTypedObject, ICommandMethodsProvider
    {
        readonly IWorldName _world;
        readonly IWorldStore _worldStore;
        readonly XLocalFeedProvider _packageFeeds;
        readonly XNuGetClient _nuGetClient;
        readonly MSBuildContext _msBuildContext;
        readonly XSecretKeyStore _publishKeyStore;
        readonly List<XGitFolder> _allGitFolders;

        readonly List<XSolutionBase> _allSolutions;
        readonly List<XSolutionBase> _allDevelopSolutions;
        readonly List<XGitFolder> _allGitFoldersWithDevelopBranchName;
        WorldContext _worldContext;

        public XSolutionCentral(
            FileSystem fileSystem,
            IWorldName world,
            IWorldStore worldStore,
            XSecretKeyStore publishKeyStore,
            XLocalFeedProvider packageFeeds,
            XNuGetClient nuGetClient,
            CommandRegister commandRegister,
            Initializer initializer )
            : base( initializer )
        {
            _world = world;
            _worldStore = worldStore;
            _packageFeeds = packageFeeds;
            _nuGetClient = nuGetClient;
            _publishKeyStore = publishKeyStore;
            _msBuildContext = new MSBuildContext( fileSystem );
            fileSystem.ServiceContainer.Add( _msBuildContext );

            _allGitFolders = new List<XGitFolder>();
            initializer.Services.Add( this );

            _allSolutions = new List<XSolutionBase>();
            _allDevelopSolutions = new List<XSolutionBase>();
            _allGitFoldersWithDevelopBranchName = new List<XGitFolder>();

            CommandProviderName = "Solutions";
            commandRegister.Register( this );
        }

        public NormalizedPath CommandProviderName { get; }

        [CommandMethod]
        public void DumpGitFolderStatus( IActivityMonitor m )
        {
            var gitFolders = _allGitFolders.Select( x => x.GitFolder );
            int gitFoldersCount = 0;
            var dirty = new List<string>();
            foreach( var git in gitFolders )
            {
                ++gitFoldersCount;
                using( m.OpenInfo( $"{git.SubPath} - branch: {git.CurrentBranchName}." ) )
                {
                    var pluginCount = $"({git.PluginManager.BranchPlugins[git.CurrentBranchName].Count} plugins)";
                    if( git.CheckCleanCommit( m ) ) m.CloseGroup( "Up-to-date. " + pluginCount );
                    else
                    {
                        dirty.Add( git.SubPath );
                        m.CloseGroup( "Dirty. "+ pluginCount );
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
        }

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

        public WorldContext GetWorldContext( IActivityMonitor m )
        {
            if( _worldContext == null )
            {
                _worldContext = WorldContext.Create(
                                    m,
                                    _world,
                                    _worldStore,
                                    _packageFeeds,
                                    _nuGetClient.NuGetClient,
                                    _publishKeyStore,
                                    AllGitFoldersWithDevelopBranchName.Select( g => g.GitFolder ),
                                    (monitor,branchName) => GetAllSolutions( monitor, true, branchName ) );

            }
            return _worldContext;
        }

        public IReadOnlyList<Solution> GetAllSolutions( IActivityMonitor m, bool reload, string branchName )
        {
            if( branchName == _world.DevelopBranchName )
            {
                return AllDevelopSolutions.Select( s => s.GetSolution( m, reload, branchName ) ).ToList();
            }
            var solutions = new List<Solution>();
            foreach( var g in _allGitFolders )
            {
                var b = g.Branches.FirstOrDefault( x => x.Name == branchName ) ?? g.DevelopBranch;
                if( b != null )
                {
                    foreach( var s in b.Solutions )
                    {
                        solutions.Add( s.GetSolution( m, reload, branchName ) );
                    }
                }
            }
            return solutions;
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

    }
}
