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
    public class XSolutionCentral : XTypedObject
    {
        readonly IWorldName _world;
        readonly IWorldStore _worldStore;
        readonly XLocalFeedProvider _packageFeeds;
        readonly XNuGetClient _nuGetClient;
        readonly MSBuildContext _msBuildContext;
        readonly XReferentialFolder _referential;
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
            XReferentialFolder referential,
            XSecretKeyStore publishKeyStore,
            XLocalFeedProvider packageFeeds,
            XNuGetClient nuGetClient,
            Initializer initializer )
            : base( initializer )
        {
            _world = world;
            _worldStore = worldStore;
            _packageFeeds = packageFeeds;
            _nuGetClient = nuGetClient;
            _referential = referential;
            _publishKeyStore = publishKeyStore;
            _msBuildContext = new MSBuildContext( fileSystem );
            _allGitFolders = new List<XGitFolder>();
            initializer.Services.Add( this );

            _allSolutions = new List<XSolutionBase>();
            _allDevelopSolutions = new List<XSolutionBase>();
            _allGitFoldersWithDevelopBranchName = new List<XGitFolder>();
        }

        internal void Register( XGitFolder g )
        {
            _allGitFolders.Add( g );
        }

        protected override bool OnCreated( Initializer initializer )
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
            return base.OnCreated( initializer );
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
                                    _referential.FileProvider,
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
