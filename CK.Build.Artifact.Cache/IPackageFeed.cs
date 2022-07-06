using CK.Build.PackageDB;
using CK.Core;
using CK.PerfectEvent;
using System.Threading.Tasks;

namespace CK.Build
{
    /// <summary>
    /// Extends a <see cref="IArtifactFeedIdentity"/> to provide capability to
    /// obtain <see cref="IPackageInstanceInfo"/> from <see cref="ArtifactInstance"/>.
    /// </summary>
    public interface IPackageFeed : IArtifactFeedIdentity
    {
        /// <summary>
        /// Gets the package information in this feed or null if it doesn't exist in this feed.
        /// Exceptions must be thrown for any access issues.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="instance">The instance to lookup.</param>
        /// <returns>The package information or null if it doesn't exist.</returns>
        Task<IPackageInstanceInfo?> GetPackageInfoAsync( IActivityMonitor monitor, ArtifactInstance instance );

        /// <summary>
        /// Raised each time a raw package information has been obtained from the feed.
        /// </summary>
        PerfectEvent<IPackageFeed, RawPackageInfoEventArgs> FeedPackageInfoObtained { get; }
    }


}
