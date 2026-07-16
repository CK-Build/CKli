using CK.Core;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Provides the current, last built, <see cref="ITagCommit"/> for a <see cref="HotBranch"/>.
/// <para>
/// This is not provided by this BranchModel plugin: this 
/// </para>
/// </summary>
public interface ITagCommitProvider
{
    /// <summary>
    /// Tries to get the current commit for a branch. On success, the commit is unambiguous: if more
    /// than a single versioned commit appears in the branch's parent commits, a build of the branch
    /// is required and this is an error.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="branch">The branch to consider.</param>
    /// <param name="ciBuild">True if the CI build must be considered.</param>
    /// <returns>The versioned tagged commit on success, null otherwise.</returns>
    ITagCommit? GetCommit( IActivityMonitor monitor, HotBranch branch, bool ciBuild );
}
