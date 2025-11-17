using System;

namespace CKli.Core;

/// <summary>
/// Exposes all we need to know about the head of a <see cref="GitRepository"/>
/// </summary>
public interface IGitHeadInfo
{
    /// <summary>
    /// Gets the head's commit sha.
    /// </summary>
    string CommitSha { get; }

    /// <summary>
    /// Gets the SHA1 signature of the <see cref="CommitSha"/> (by default) or
    /// the one of any tree or blob inside.
    /// </summary>
    /// <param name="path">The object's path. When empty, it is the SHA of the Tree (the "content SHA")) that is retrieved.</param>
    /// <returns>The SHA or null if not found.</returns>
    string? GetSha( string? path = null );

    /// <summary>
    /// Gets the current commit's message.
    /// </summary>
    string Message { get; }

    /// <summary>
    /// Gets the commit message.
    /// </summary>
    DateTimeOffset CommitDate { get; }

    /// <summary>
    /// Gets the number of commit that are ahead of the origin.
    /// 0 mean that there a no commit ahead of origin (there's nothing to push).
    /// Null if there is no origin (the branch is not tracked).
    /// </summary>
    int? AheadOriginCommitCount { get; }

    /// <summary>
    /// Gets the number of commit that are behind of the origin.
    /// 0 mean that there a no commit behind of origin (there's nothing to pull).
    /// Null if there is no origin (the branch is not tracked).
    /// </summary>
    int? BehindOriginCommitCount { get; }

}
