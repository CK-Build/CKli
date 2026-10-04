namespace CKli.Core;

/// <summary>
/// What a merge would do: see <see cref="GitRepository.PredictMerge"/>.
/// </summary>
public enum MergeOutcome
{
    /// <summary>
    /// Nothing to merge: the target already contains it, or it brings no content.
    /// </summary>
    UpToDate,

    /// <summary>
    /// The target is fast-forwarded.
    /// </summary>
    FastForward,

    /// <summary>
    /// A merge commit is created (with its sides aligned when they conflict, see <see cref="MergeSidesAligner"/>).
    /// </summary>
    Merge,

    /// <summary>
    /// The merge conflicts beyond what the sides alignment resolves: it would be left in progress in the working folder.
    /// </summary>
    Conflict,

    /// <summary>
    /// The merge cannot be computed (the reason has been logged as an error).
    /// </summary>
    Failed
}
