using CK.Env;
using CK.Env.MSBuild;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    /// <summary>
    /// Captures an input for the <see cref="GlobalReleaser"/>.
    /// All Git folders must have been checked out on the <see cref="IWorldName.DevelopBranchName"/>
    /// and optionally a fetch all has been done.
    /// </summary>
    public class GlobalReleaseContext
    {
        /// <summary>
        /// Gets the curent world.
        /// </summary>
        public IWorldName World { get; }

        /// <summary>
        /// Gets whether a fetch all has been done.
        /// </summary>
        public bool FetchAllDone { get; }

        /// <summary>
        /// Gets the sets of all git folders without duplicates with their respective SimpleGitVersion <see cref="RepositoryInfo"/>.
        /// </summary>
        public IReadOnlyDictionary<GitFolder,RepositoryInfo> AllGitFolders { get; }

        /// <summary>
        /// Gets the set of up-to-date solutions from <see cref="IWorldName.DevelopBranchName"/>.
        /// </summary>
        public IReadOnlyList<Solution> AllSolutions { get; }

        /// <summary>
        /// Initalizes a new context.
        /// </summary>
        /// <param name="w">The world.</param>
        /// <param name="f">Whether a fetch all has been doce.</param>
        /// <param name="gits">The git folders with the repository information.</param>
        /// <param name="solutions">The set of solutions to consider.</param>
        public GlobalReleaseContext( IWorldName w, bool f, IReadOnlyDictionary<GitFolder, RepositoryInfo> gits, IReadOnlyList<Solution> solutions )
        {
            World = w;
            FetchAllDone = f;
            AllGitFolders = gits;
            AllSolutions = solutions;
        }
    }
}
