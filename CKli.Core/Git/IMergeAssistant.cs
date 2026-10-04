using CK.Core;
using LibGit2Sharp;
using System;

namespace CKli.Core;

/// <summary>
/// Helps the merges of a command (see <see cref="World.SetMergeAssistant(IMergeAssistant)"/>): a plugin that knows
/// how to align the sides of a merge that conflicts, and what a branch name means in a repository, implements it.
/// <para>
/// Without an assistant, nothing is aligned and a branch name is taken literally.
/// </para>
/// </summary>
public interface IMergeAssistant
{
    /// <summary>
    /// Opens a session. A command opens it once the merges that need no help are done (the World is settled): the
    /// session caches what it computes for the duration of the merges that conflict, and the command disposes it.
    /// The session is used sequentially: it doesn't have to be thread safe.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The session.</returns>
    IMergeSession OpenSession( IActivityMonitor monitor );
}

/// <summary>
/// A session opened by <see cref="IMergeAssistant.OpenSession(IActivityMonitor)"/>.
/// </summary>
public interface IMergeSession : IDisposable
{
    /// <summary>
    /// Gets the branch of the repository that a person works on when they name <paramref name="branchName"/>
    /// (the "--branch" option of "ckli pull" and "ckli push"): a merge that conflicts there is left in progress.
    /// A branch that only exists on the remote may be created locally. Null when the repository has no such branch.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="branchName">The name given by the person.</param>
    /// <returns>The branch or null.</returns>
    Branch? GetWorkingBranch( IActivityMonitor monitor, Repo repo, string branchName );

    /// <summary>
    /// Gets the aligner of the sides of a merge into the <paramref name="target"/> branch. Null when there is
    /// nothing to align on this branch.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="target">The branch that receives the merge.</param>
    /// <returns>The aligner or null.</returns>
    MergeSidesAligner? GetAligner( IActivityMonitor monitor, Repo repo, Branch target );
}
