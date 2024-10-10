using CK.Core;

namespace CKli.Core;

/// <summary>
/// Result of a <see cref="GitRepository.Pull(IActivityMonitor, LibGit2Sharp.MergeFileFavor, LibGit2Sharp.FastForwardStrategy)"/>.
/// </summary>
public enum MergeResult
{
    /// <summary>
    /// An error occurred.
    /// </summary>
    Error,

    /// <summary>
    /// Merge resulted in conflicts.
    /// </summary>
    ErrorConflicts,

    /// <summary>
    /// Merge was up-to-date.
    /// </summary>
    UpToDate,

    /// <summary>
    /// Fast-forward merge.
    /// </summary>
    FastForward,

    /// <summary>
    /// Non-fast-forward merge.
    /// </summary>
    NonFastForward,

}
