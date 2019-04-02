using System;

namespace CK.Env
{
    /// <summary>
    /// Defines composable constraints on versions.
    /// </summary>
    [Flags]
    public enum ReleaseConstraint
    {
        /// <summary>
        /// No constraint.
        /// </summary>
        None = 0,

        /// <summary>
        /// The version must be a pre-release.
        /// </summary>
        MustBePreRelease = 1,

        /// <summary>
        /// The version must indicate new features.
        /// This does not apply if <see cref="MustBePreRelease"/> is set.
        /// </summary>
        HasFeatures = 2,

        /// <summary>
        /// The version must indicate a breaking change.
        /// This does not apply if <see cref="MustBePreRelease"/> is set.
        /// </summary>
        HasBreakingChanges = 4 | HasFeatures

    }
}
