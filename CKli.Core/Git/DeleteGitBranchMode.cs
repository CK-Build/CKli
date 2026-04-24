using CK.Core;
using LibGit2Sharp;
using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Applies to <see cref="GitRepository.DeleteBranch(IActivityMonitor, Branch, DeleteGitBranchMode)"/>.
/// </summary>
public enum DeleteGitBranchMode
{
    /// <summary>
    /// Only deletes the provided branch.
    /// </summary>
    LocalOnly,

    /// <summary>
    /// If the branch is tracking, also deletes the tracked branch.
    /// This deletes the tracked branch locally, not on its remote.
    /// </summary>
    WithTrackedBranch,

    /// <summary>
    /// Fully deletes the branch if the branch is tracked and has a remote name.
    /// A <see cref="GitRepository.Push(IActivityMonitor, Remote, UsernamePasswordCredentials?, IEnumerable{string})"/>
    /// is done to remove the branch on the <see cref="Branch.RemoteName"/>.
    /// </summary>
    WithTrackedAndRemoteBranch
}

