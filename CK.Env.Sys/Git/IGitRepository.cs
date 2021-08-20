using CK.Core;
using CK.Text;
using CSemVer;
using SimpleGitVersion;
using System;
using System.Collections.Generic;

namespace CK.Env
{
    public interface IGitRepository
    {
        /// <summary>
        /// Gets the Origin URL of the Git repository
        /// </summary>
        Uri OriginUrl { get; }

        /// <summary>
        /// Ensures that a local branch exists.
        /// If the branch is created, it will point at the same commit as the current <see cref="Head"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The branch name.</param>
        void EnsureBranch( IActivityMonitor m, string branchName, bool noWarnOnCreate = false );

        /// <summary>
        /// Gets the full physical path of the Git folder.
        /// </summary>
        NormalizedPath FullPhysicalPath { get; }

        /// <summary>
        /// Get the path relative to the FileSystem.
        /// </summary>
        NormalizedPath SubPath { get; }

        /// <summary>
        /// Gets the head information.
        /// </summary>
        IGitHeadInfo Head { get; }

        /// <summary>
        /// Gets the simple git version <see cref="ICommitInfo"/> from a branch.
        /// Returns null if an error occurred or if RepositoryInfo.xml has not been successfully read.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">Defaults to <see cref="CurrentBranchName"/>.</param>
        /// <returns>The RepositoryInfo or null if it cannot be obtained.</returns>
        ICommitInfo? ReadVersionInfo( IActivityMonitor m, string? branchName = null );

        /// <summary>
        /// Gets the sha of the given branch tip or null if the branch doesn't exist.
        /// </summary>
        /// <param name="m">The monitor ti use.</param>
        /// <param name="branchName">The branch name. Must not be null or empty.</param>
        /// <returns>The Sha or null.</returns>
        string GetBranchSha( IActivityMonitor m, string branchName );

        /// <summary>
        /// Gets whether the head can be amended: the current branch
        /// is not tracked or the current commit is ahead of the remote branch.
        /// </summary>
        bool CanAmendCommit { get; }

        /// <summary>
        /// Gets the set of <see cref="DirectoryDiff"/> for a set of folders from the current head.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="previousVersionCommitSha">Previous commit.</param>
        /// <param name="solutionRelativeFolders">Folders for which differences must be computed.</param>
        /// <returns>The set of differences or null on error.</returns>
        IDiffResult GetDiff( IActivityMonitor m, string previousVersionCommitSha, IEnumerable<IDiffRoot> solutionRelativeFolders );

        /// <summary>
        /// Commits any pending changes.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="commitMessage">
        /// Required commit message.
        /// This is ignored when <paramref name="amendIfPossible"/> and <see cref="CanAmendCommit"/> are both true.
        /// </param>
        /// <param name="amendIfPossible">
        /// True to call <see cref="AmendCommit"/> if <see cref="CanAmendCommit"/>. is true.
        /// </param>
        /// <returns>True on success, false on error.</returns>
        CommittingResult Commit( IActivityMonitor m, string commitMessage, CommitBehavior commitBehavior = CommitBehavior.CreateNewCommit );

        /// <summary>
        /// Amends the current commit, optionally changing its message and/or its date.
        /// <see cref="CanAmendCommit"/> must be true otherwise an <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="editMessage">
        /// Optional message transformer. By returning null, the operation is canceled and false is returned.
        /// </param>
        /// <param name="editDate">
        /// Optional date transformer. By returning null, the operation is canceled and false is returned.
        /// </param>
        /// <param name="skipIfNothingToCommit">
        /// By default, no amend is done if working folder is up to date.
        /// False will force the amend to be done if the date or message changed even if the working folder is clean.
        /// </param>
        /// <returns>True on success, false on error.</returns>
        CommittingResult AmendCommit( IActivityMonitor m, Func<string, string>? editMessage = null, Func<DateTimeOffset, DateTimeOffset?>? editDate = null, bool skipIfNothingToCommit = true );

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
        /// Sets a version lightweight tag on the current head.
        /// An error is logged if the version tag already exists on another commit that the head.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="v">The version to set.</param>
        /// <returns>True on success, false on error.</returns>
        bool SetVersionTag( IActivityMonitor m, SVersion v );

        /// <summary>
        /// Removes a version lightweight tag from the repository.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="v">The version to remove.</param>
        /// <returns>True on success, false on error.</returns>
        bool ClearVersionTag( IActivityMonitor m, SVersion v );

        /// <summary>
        /// Pushes a version lightweight tag to the 'origin' remote.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="v">The version to push.</param>
        /// <returns>True on success, false on error.</returns>
        bool PushVersionTag( IActivityMonitor m, SVersion v );

        /// <summary>
        /// Pushes changes from a branch to the origin.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">Local branch name. When null, the <see cref="CurrentBranchName"/> is used.</param>
        /// <returns>True on success, false on error.</returns>
        bool Push( IActivityMonitor m, string? branchName = null );

        /// <summary>
        /// Resets a branch to a previous commit or deletes the branch when <paramref name="commitSha"/> is null or empty.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The branch name.</param>
        /// <param name="commitSha">The commit sha to restore.</param>
        /// <returns>True on success, false on error.</returns>
        bool ResetBranchState( IActivityMonitor m, string branchName, string commitSha );

        /// <summary>
        /// Fetches 'origin' (or all remotes) branches into this repository.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>
        /// Success is true on success, false on error.
        /// </returns>
        bool FetchBranches( IActivityMonitor m, bool originOnly = true );

        /// <summary>
        /// Checks out a branch: calls <see cref="FetchBranches"/> and pulls remote 'origin' branch changes.
        /// There must not be any uncommitted changes on the current head.
        /// The branch must exist locally or on the 'origin' remote.
        /// If the branch exists only in the "origin" remote, a local branch is automatically
        /// created that tracks the remote one.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="branchName">The local name of the branch.</param>
        /// <param name="skipFetchBranches">True to not call <see cref="FetchBranches"/>.</param>
        /// <param name="skipPullMerge">True to not "pull merge" from the remote after having checked out the branch.</param>
        /// <returns>
        /// Success is true on success, false on error (such as merge conflicts) and in case of success,
        /// the result states whether a reload should be required or if nothing changed.
        /// </returns>
        (bool Success, bool ReloadNeeded) Checkout( IActivityMonitor m, string branchName, bool skipFetchBranches = false, bool skipPullMerge = false );

        /// <summary>
        /// Pulls current branch by merging changes from remote 'origin' branch into this repository.
        /// The current head must be clean.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>
        /// Success is true on success, false on error (such as merge conflicts) and in case of success,
        /// the result states whether a reload should be required or if nothing changed.
        /// </returns>
        (bool Success, bool ReloadNeeded) Pull( IActivityMonitor m );

        /// <summary>
        /// Checkouts the <see cref="IWorldName.MasterBranchName"/>, always merging <see cref="IWorldName.DevelopBranchName"/> into it.
        /// If the repository is not already on the 'master' branch, it must be on 'develop' and on a clean commit.
        /// The 'master' branch is created if needed and checked out.
        /// 'develop' branch is always merged into it.
        /// If the merge fails, a manual operation is required.
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
        /// If the repository is not on the 'local' branch, it must be on 'develop' (a <see cref="Commit"/> is done to save any
        /// current work if <paramref name="autoCommit"/> is true), the 'local' branch is created if needed and checked out.
        /// 'develop' branch is always merged into it, privileging file modifications from the 'develop' branch.
        /// If the merge fails, a manual operation is required.
        /// On success, the solution inside should be purely local: there should not be any possible remote interactions (except
        /// possibly importing fully external packages).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="autoCommit">False to require the working folder to be clean and not automatically creating a commit.</param>
        /// <returns>True on success, false on error.</returns>
        bool SwitchDevelopToLocal( IActivityMonitor m, bool autoCommit = true );

        /// <summary>
        /// Checkouts '<see cref="IWorldName.DevelopBranchName"/>' branch and merges current 'local' in it.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        bool SwitchLocalToDevelop( IActivityMonitor m );

    }
}
