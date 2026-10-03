namespace CKli.BranchModel.Plugin;

/// <summary>
/// What a merge would do: see <see cref="HotBranch.PredictSynchronize"/> and <see cref="HotBranch.PredictClose"/>.
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
    /// A merge commit is created (with its package versions aligned when they conflict).
    /// </summary>
    Merge,

    /// <summary>
    /// The merge conflicts beyond the package versions: it would be left in progress in the working folder.
    /// </summary>
    Conflict,

    /// <summary>
    /// The merge cannot be computed (the reason has been logged as an error).
    /// </summary>
    Failed
}
