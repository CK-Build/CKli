using LibGit2Sharp;
using System;

namespace CKli.Core;

public sealed partial class GitTagInfo
{
    /// <summary>
    /// Captures <see cref="LocalRemoteTag.Diff"/>.
    /// </summary>
    [Flags]
    public enum TagDiff
    {
        /// <summary>
        /// Both <see cref="LocalRemoteTag.Local"/> and <see cref="LocalRemoteTag.Remote"/> exist and are identical.
        /// </summary>
        None = 0,

        /// <summary>
        /// Only the <see cref="LocalRemoteTag.Local"/> exists.
        /// </summary>
        LocalOnly = 1,

        /// <summary>
        /// Only the <see cref="LocalRemoteTag.Remote"/> exists.
        /// </summary>
        RemoteOnly = 2,

        /// <summary>
        /// Both tags exist but the <see cref="LocalRemoteTag.Local"/> isa an annotated
        /// one and the <see cref="LocalRemoteTag.Remote"/> is a lightweight tag.
        /// </summary>
        LocalAnnotated = 4,

        /// <summary>
        /// Both tags exist but the <see cref="LocalRemoteTag.Remote"/> isa an annotated
        /// one and the <see cref="LocalRemoteTag.Local"/> is a lightweight tag.
        /// </summary>
        RemoteAnnotated = 8,

        /// <summary>
        /// Both tags exist and are annotated tags but their <see cref="TagAnnotation.Message"/> differ.
        /// </summary>
        AnnotationMessageDiffer = 16,

        /// <summary>
        /// Both tags exist and are annotated tags but their <see cref="TagAnnotation.Tagger"/> differ.
        /// </summary>
        AnnotationTaggerDiffer = 32,

        /// <summary>
        /// Only <see cref="LocalRemoteTag.Local"/> or <see cref="LocalRemoteTag.Remote"/> exists (not both)
        /// because the other one targets another commit. <see cref="LocalRemoteTag.Conflict"/> is necessarily not null
        /// and belongs to the "other side".
        /// </summary>
        CommitConflict = 64
    }
}
