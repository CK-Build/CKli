using System;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagInfo
{
    /// <summary>
    /// Rebuild options for <see cref="TryGetCommitBuildInfo"/>.
    /// </summary>
    [Flags]
    public enum RebuildMode
    {
        /// <summary>
        /// Disallow any rebuild: the build commit must not already have been built
        /// and the target version must not already exists.  
        /// </summary>
        None = 0,

        /// <summary>
        /// Allow the build commit to have already been built and don't check that the immediate previous stable release belongs
        /// to the build commit's ancestors.
        /// </summary>
        AllowRebuildCommit = 1,

        /// <summary>
        /// Whether checking that the immediate previous stable release belongs to the build commit's ancestors must be done.
        /// </summary>
        CheckPreviousVersion = 2,

        /// <summary>
        /// Allow the build commit to have already been built and check that the immediate previous stable release belongs
        /// to the build commit's ancestors.
        /// </summary>
        AllowRebuildCommitAndCheckPrevious = AllowRebuildCommit | CheckPreviousVersion,

        /// <summary>
        /// Allow the target version to already exists on another commit and don't check that the immediate previous
        /// stable release belongs to the build commit's ancestors.
        /// </summary>
        AllowRebuildVersion = 4,

        /// <summary>
        /// Allow the target version to already exists on another commit and check that the immediate previous stable release
        /// belongs to the build commit's ancestors.
        /// </summary>
        AllowRebuildVersionAndCheckPrevious = AllowRebuildVersion | CheckPreviousVersion,
    }
}

