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
        readonly GlobalContext.World _world;
        readonly MSBuildContext _msBuildContext;
        readonly List<XSolutionBase> _allSolutions;
        readonly List<XSolutionBase> _allDevelopSolutions;
        readonly HashSet<XGitFolder> _allGitFoldersWithDevelopBranchName;

        public XSolutionCentral(
            FileSystem fileSystem,
            GlobalContext.World world,
            Initializer initializer )
            : base( initializer )
        {
            _world = world;
            _msBuildContext = new MSBuildContext( fileSystem );
            initializer.Services.Add( this );
            _allSolutions = new List<XSolutionBase>();
            _allDevelopSolutions = new List<XSolutionBase>();
            _allGitFoldersWithDevelopBranchName = new HashSet<XGitFolder>();
        }

        internal void Register( XSolutionBase s )
        {
            _allSolutions.Add( s );
            if( s.GitBranch.Name == _world.DevelopBranchName )
            {
                _allGitFoldersWithDevelopBranchName.Add( s.GitBranch.Parent );
                _allDevelopSolutions.Add( s );
            }
        }

        /// <summary>
        /// Gets the current world.
        /// </summary>
        public GlobalContext.World World => _world;

        /// <summary>
        /// Gets the MSBuild context that handles solution and project files.
        /// </summary>
        public MSBuildContext MSBuildContext => _msBuildContext;

        /// <summary>
        /// Gets all Git folders that have a <see cref="IWorldName.DevelopBranchName"/>
        /// and at least one solution in it.
        /// </summary>
        public IReadOnlyCollection<XGitFolder> AllGitFoldersWithDevelopBranchName => _allGitFoldersWithDevelopBranchName;

        /// <summary>
        /// Gets all the solutions regardless of their type or branch.
        /// </summary>
        public IReadOnlyList<XSolutionBase> AllSolutions => _allSolutions;

        /// <summary>
        /// Gets all the solutions regardless of their type in branch <see cref="World"/>.<see cref="GlobalContext.World.DevelopBranchName">DevelopBranchName</see>.
        /// The order is important here: Primary solutions necessarily appear before any of their secondary solutions.
        /// <see cref="GetGlobalReleaseContext"/> relies on this order to avois useless reloading.
        /// </summary>
        public IReadOnlyList<XSolutionBase> AllDevelopSolutions => _allDevelopSolutions;

        /// <summary>
        /// Gets the actual, up to date, set of <see cref="Solution"/> (primary or secodary) after a <see cref="GitFolder.CheckoutAndPull"/>
        /// on <see cref="IWorldName.DevelopBranchName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="fetchAll">
        /// True to fecth all from 'origin' remotes of the Git folders
        /// This is required before a global release but not for a ci-build.
        /// </param>
        /// <returns>The global context or null on error.</returns>
        public GlobalReleaseContext GetGlobalReleaseContext( IActivityMonitor m, bool fetchAll )
        {
            var gitInfos = new Dictionary<GitFolder, RepositoryInfo>();
            var dirty = new List<GitFolder>();
            foreach( var g in AllGitFoldersWithDevelopBranchName.Select( g => g.GitFolder ) )
            {
                var (success, needReload) = (true, false);
                //g.CheckoutAndPull( m, World.DevelopBranchName, fetchAll );
                if( !success ) return null;
                var r = g.ReadVersionInfo( m, World.DevelopBranchName );
                if( r == null ) return null;
                gitInfos.Add( g, r );
                if( needReload ) dirty.Add( g );
            }
            List<Solution> solutions = new List<Solution>();
            foreach( var s in AllDevelopSolutions )
            {
                Solution upToDate = s.GetSolution( m, s is XPrimarySolution && dirty.Contains( s.GitBranch.Parent.GitFolder ) );
                if( upToDate == null ) return null;
                solutions.Add( upToDate );
            }
            return new GlobalReleaseContext( World, fetchAll, gitInfos, solutions );
        }
    }
}
