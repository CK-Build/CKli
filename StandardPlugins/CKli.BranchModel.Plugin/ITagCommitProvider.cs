using CK.Core;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Provides the current, last built, <see cref="ITagCommit"/> for a <see cref="HotBranch"/>.
/// <para>
/// External implementation must be provided to <see cref="HotBranch.Synchronize(IActivityMonitor, ITagCommitProvider, BranchLinkType)"/> 
/// </para>
/// </summary>
public interface ITagCommitProvider
{
    /// <summary>
    /// Tries to get the current commit for a branch.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="branch">The branch to consider. <see cref="HotBranch.Exists"/> is necessarily true.</param>
    /// <param name="allowCI">
    /// True to get a ".ci" version if it exists.
    /// This is used when the subordinated branch is configured with <see cref="BranchLinkType.CI"/>.
    /// </param>
    /// <returns>The versioned tagged commit on success, null otherwise.</returns>
    ITagCommit? GetCommit( IActivityMonitor monitor, HotBranch branch, bool allowCI );
}
