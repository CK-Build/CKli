using System;

namespace CK.Build
{
    /// <summary>
    /// Captures the <see cref="PackageInstance.State"/> and <see cref="IPackageInstanceInfo.State"/>.
    /// </summary>
    [Flags]
    public enum PackageState : byte
    {
        /// <summary>
        /// Regular package instance.
        /// </summary>
        None = 0,

        /// <summary>
        /// The package instance has been unlisted in at least one feed
        /// (it cannot be found anymore in this feed but is still available).
        /// </summary>
        Unlisted = 1,

        /// <summary>
        /// The package instance has been deprecated in at least one feed.
        /// </summary>
        Deprecated = 2,

        /// <summary>
        /// The package instance doesn't exist in any feed but is however referenced
        /// by a <see cref="PackageInstance.Reference"/>.
        /// </summary>
        Ghost = 4
    }
}
