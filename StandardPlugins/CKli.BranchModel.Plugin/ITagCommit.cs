using CK.Core;
using CKli.Core;
using LibGit2Sharp;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Minimal abstraction of a versioned tagged commit in a repository.
/// See <see cref="ITagCommitProvider"/>.
/// </summary>
public interface ITagCommit
{
    /// <summary>
    /// Gets the repository.
    /// </summary>
    Repo Repo { get; }

    /// <summary>
    /// Gets the version.
    /// </summary>
    SVersion Version { get; }

    /// <summary>
    /// Gets the commit.
    /// </summary>
    Commit Commit { get; }
}
