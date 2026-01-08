using CK.Core;

namespace CKli.Core;

/// <summary>
/// Result of a <see cref="GitRepository.FetchMerge(IActivityMonitor, LibGit2Sharp.MergeFileFavor, LibGit2Sharp.FastForwardStrategy)"/>.
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

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public static class MergeResultExtensions
{
    public static bool IsError( this MergeResult result ) => result is MergeResult.Error or MergeResult.ErrorConflicts;
    public static bool IsSuccess( this MergeResult result ) => result is not MergeResult.Error and not MergeResult.ErrorConflicts;
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
