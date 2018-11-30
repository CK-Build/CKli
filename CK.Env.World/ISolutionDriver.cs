using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Defines the basic requirement for a solution that the centralized world can handle.
    /// </summary>
    public interface ISolutionDriver
    {
        /// <summary>
        /// Gets the Git repository.
        /// This can never be null.
        /// </summary>
        IGitRepository GitRepository { get; }

        /// <summary>
        /// Gets the branch name.
        /// This can never be null.
        /// </summary>
        string BranchName { get; }

        /// <summary>
        /// Updates projects dependencies and saves the solution and its updated projects.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageInfos">The .</param>
        /// <returns>True on success, false on error.</returns>
        bool UpdatePackageDependencies( IActivityMonitor monitor, IEnumerable<UpdatePackageInfo> packageInfos );

    }
}
