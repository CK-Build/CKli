
namespace CK.Env
{
    /// <summary>
    /// Defines the current state of a World.
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
    }
}
