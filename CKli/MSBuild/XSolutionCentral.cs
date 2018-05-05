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
        readonly XPublishedPackageFeeds _packageFeeds;
        readonly MSBuildContext _msBuildContext;
        readonly XReferentialFolder _referential;
        readonly List<XSolutionBase> _allSolutions;
        readonly List<XSolutionBase> _allDevelopSolutions;
        readonly List<XGitFolder> _allGitFoldersWithDevelopBranchName;
        WorldContext _worldContext;

        public XSolutionCentral(
            FileSystem fileSystem,
            IWorldName world,
            IWorldStore worldStore,
            XReferentialFolder referential,
            XPublishedPackageFeeds packageFeeds,
            Initializer initializer )
            : base( initializer )
        {
            _world = world;
            _worldStore = worldStore;
            _packageFeeds = packageFeeds;
            _referential = referential;
            _msBuildContext = new MSBuildContext( fileSystem );
            initializer.Services.Add( this );
            _allSolutions = new List<XSolutionBase>();
            _allDevelopSolutions = new List<XSolutionBase>();
            _allGitFoldersWithDevelopBranchName = new List<XGitFolder>();
            initializer.InitializationState.Add( this, new HashSet<(int, XGitFolder)>() );
        }

        internal void Register( XSolutionBase s )
        {
            _allSolutions.Add( s );
            if( s.GitBranch.Name == _world.DevelopBranchName )
            {
                if( !_allGitFoldersWithDevelopBranchName.Contains( s.GitBranch.Parent ) )
                {
                    _allGitFoldersWithDevelopBranchName.Add( s.GitBranch.Parent );
                }
                _allDevelopSolutions.Add( s );
            }
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
                                    _referential.FileProvider,
                                    AllGitFoldersWithDevelopBranchName.Select( g => g.GitFolder ),
                                    ( monitor, branchName ) => AllDevelopSolutions.Select( s => s.GetSolution( monitor, true, branchName ) ).ToList() );

            }
            return _worldContext;
        }

        /// <summary>
        /// Gets the MSBuild context that handles solutions and projects files.
        /// </summary>
        public MSBuildContext MSBuildContext => _msBuildContext;

        /// <summary>
        /// Gets all Git folders that have a <see cref="IWorldName.DevelopBranchName"/>
        /// and at least one solution in it in the order of their definition in the World xml file.
        /// </summary>
        public IReadOnlyList<XGitFolder> AllGitFoldersWithDevelopBranchName => _allGitFoldersWithDevelopBranchName;

        /// <summary>
        /// Gets all the solutions regardless of their type or branch in the order of their definition in the World xml file.
        /// </summary>
        public IReadOnlyList<XSolutionBase> AllSolutions => _allSolutions;

        /// <summary>
        /// Gets all the solutions regardless of their type in branch <see cref="World"/>.<see cref="GlobalContext.World.DevelopBranchName">DevelopBranchName</see>.
        /// The order is important here (order of their definition in the World xml file): Primary solutions necessarily
        /// appear before any of their secondary solutions.
        /// </summary>
        public IReadOnlyList<XSolutionBase> AllDevelopSolutions => _allDevelopSolutions;

    }
}
