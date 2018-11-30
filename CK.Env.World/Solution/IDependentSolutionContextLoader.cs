using System;
using System.Collections.Generic;
using CK.Core;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Knons how to compute a <see cref="IDependentSolutionContext"/> from a
    /// set of actual solutions in a given branch.
    /// </summary>
    public interface IDependentSolutionContextLoader
    {
        /// <summary>
        /// Computes a <see cref="IDependentSolutionContext"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="repositories">Set of repositories to consider.</param>
        /// <param name="branchName">Branch name in the repositories to consider.</param>
        /// <param name="forceReload">True to force the reload of all solutions.</param>
        /// <returns>The dependeny context or null on error.</returns>
        IDependentSolutionContext Load( IActivityMonitor m, IEnumerable<IGitRepository> repositories, string branchName, bool forceReload );

    }
}
