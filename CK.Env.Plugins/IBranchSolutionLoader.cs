using CK.Core;
using CK.Env.MSBuild;
using CK.Text;
using System.Collections.Generic;

namespace CK.Env.Plugins
{
    /// <summary>
    /// Gives access to the primary <see cref="Solution"/> and its secondary solutions
    /// from a branch or from other branches.
    /// </summary>
    public interface IBranchSolutionLoader
    {
        /// <summary>
        /// Gets the branch path that defines the solution.
        /// </summary>
        NormalizedPath BranchPathDefiner { get; }

        /// <summary>
        /// Obtains the primary solution from this branch (or from another branch) with all its existing secondary solutions
        /// available in <see cref="Solution.LoadedSecondarySolutions"/>
        /// This is null if no primary solution is defined for the <see cref="BranchPathDefiner"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="reload">True to reload the solution.</param>
        /// <param name="actualBranchName">Optional other branch for which the solution must be loaded.</param>
        /// <returns>The primary solution or null.</returns>
        Solution GetPrimarySolution( IActivityMonitor m, bool reload, string actualBranchName = null );

    }
}
