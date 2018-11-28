using CK.Core;
using CK.Env.MSBuild;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.Plugins
{
    /// <summary>
    /// Gives access to the primary <see cref="Solution"/> and its secondary solutions
    /// from a branch or from other branches.
    /// </summary>
    public interface IBranchSolutionLoader
    {
        /// <summary>
        /// Gets the branch path that defines the solutions.
        /// </summary>
        NormalizedPath BranchPathDefiner { get; }

        /// <summary>
        /// Obtains the expected solution paths from this branch or from another branch.
        /// This list is empty if there is no primary solution defined otherwise the first path is the primary solution one.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="projectToBranchName">Optional other branch for which the solution should exist.</param>
        /// <returns>The paths. Empty if no primary solution is defined for the <see cref="BranchPathDefiner"/>.</returns>
        IReadOnlyList<NormalizedPath> GetAllSolutionFilePaths( IActivityMonitor m, string projectToBranchName = null );

        /// <summary>
        /// Obtains the primary solution from this branch (or from another branch) with all its existing secondary solutions
        /// available in <see cref="Solution.LoadedSecondarySolutions"/>
        /// This is null if no primary solution is defined for the <see cref="BranchPathDefiner"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="reload">True to reload the solution.</param>
        /// <param name="projectToBranchName">Optional other branch for which the solution must be loaded.</param>
        /// <returns>The primary solution or null.</returns>
        Solution GetPrimarySolution( IActivityMonitor m, bool reload, string projectToBranchName = null );

    }
}