using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public interface IGitRepository
    {
        /// <summary>
        /// Gets the version information from a branch.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">Defaults to <see cref="CurrentBranchName"/>.</param>
        /// <returns>The commit version info or null if it it cannot be obtained.</returns>
        CommitVersionInfo GetCommitVersionInfo( IActivityMonitor m, string branchName = null );

        /// <summary>
        /// Gets the current branch name (name of the repository's HEAD).
        /// </summary>
        string CurrentBranchName { get; }

        /// <summary>
        /// Gets the standard git status, based on the <see cref="CurrentBranchName"/>.
        /// </summary>
        StandardGitStatus StandardGitStatus { get; }

    }
}
