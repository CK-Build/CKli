
namespace CK.Env
{
    /// <summary>
    /// Core status of the build system that applies to a whole world.
    /// </summary>
    public enum GlobalWorkStatus
    {
        /// <summary>
        /// No operation are currently in progress.
        /// The <see cref="StandardGitStatus"/> reflects the repository status.
        /// </summary>
        Idle,

        /// <summary>
        /// Switch from develop to local branches.
        /// </summary>
        SwitchingToLocal,

        /// <summary>
        /// Switching back to develop.
        /// This requires remote accesses. 
        /// </summary>
        SwitchingToDevelop,

        /// <summary>
        /// Releasing the stack. Once done, the release can be published
        /// or canceled.
        /// </summary>
        Releasing,

        /// <summary>
        /// Waiting for release to be canceled or publisehd.
        /// </summary>
        WaitingReleaseConfirmation,

        /// <summary>
        /// Release is being canceled.
        /// </summary>
        CancellingRelease,

        /// <summary>
        /// Release is being published.
        /// </summary>
        PublishingRelease,

        /// <summary>
        /// Any other operation that puts the build system in a non <see cref="Idle"/> state.
        /// Specific detailed status must be handled specifically. 
        /// </summary>
        OtherOperation
    }
}
