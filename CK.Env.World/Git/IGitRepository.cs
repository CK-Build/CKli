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

        /// <summary>
        /// Checks that the current head is a clean commit (working directory is clean and no staging files exists).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True if the current head is clean, false otherwise.</returns>
        bool CheckCleanCommit( IActivityMonitor m );

        /// <summary>
        /// Attempts to check out a branch and pull any 'origin' changes.
        /// There must not be any uncommitted changes on the current head.
        /// The branch must exist locally or on the 'origin' remote.
        /// If the branch exists only in the "origin" remote, a local branch is automatically
        /// created that tracks the remote one.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="branchName">The local name of the branch.</param>
        /// <param name="alwaysPullAllBranches">
        /// True to always call <see cref="PullAllBranches"/> even if the local branch already exists.
        /// When false, all the remote branches are pulled only if the local branch does not already exist.
        /// </param>
        /// <returns>
        /// Success is true on success, false on error (such as merge conflicts) and in case of success,
        /// the result states whether a reload should be required or if nothing changed.
        /// </returns>
        (bool Success, bool ReloadNeeded) CheckoutAndPull( IActivityMonitor m, string branchName, bool alwaysPullAllBranches = false );

        /// <summary>
        /// Fetches 'origin' (or all remotes) branches and merge changes into this repository.
        /// The current head must be clean.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="originOnly">False to pull all remotes, including 'origin'.</param>
        /// <returns>
        /// Success is true on success, false on error (such as merge conflicts) and in case of success,
        /// the result states whether a reload should be required or if nothing changed.
        /// </returns>
        (bool Success, bool ReloadNeeded) PullAllBranches( IActivityMonitor m, bool originOnly = true );

        /// <summary>
        /// Checkouts the <see cref="IWorldName.MasterBranchName"/>, always merging <see cref="IWorldName.DevelopBranchName"/> into it.
        /// If the repository is not already on the 'master' branch, it must be on 'develop' and on a clean commit.
        /// The 'master' branch is created if needed and checked out.
        /// 'develop' branch is always merged into it.
        /// If the the merge fails, a manual operation is required.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        bool SwitchDevelopToMaster( IActivityMonitor m );

        /// <summary>
        /// Simple safe check out of the <see cref="IWorldName.DevelopBranchName"/> (that must exist) from
        /// the <see cref="IWorldName.MasterBranchName"/> (that may not exist).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        bool SwitchMasterToDevelop( IActivityMonitor m );

        /// <summary>
        /// Checkouts the <see cref="IWorldName.LocalBranchName"/>, always merging <see cref="IWorldName.DevelopBranchName"/> into it.
        /// If the repository is not on the 'local' branch, it must be on 'develop': a <see cref="Commit"/> is done to save any
        /// current work, the 'local' branch is created if needed and checked out.
        /// 'develop' branch is always merged into it.
        /// If the the merge fails, a manual operation is required.
        /// On success, the solution inside should be purely local: there should not be any possible remote interactions (except
        /// possibly importing packages).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        bool SwitchDevelopToLocal( IActivityMonitor m, bool autoCommit = true );

    }
}
