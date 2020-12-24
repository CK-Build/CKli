using CK.Core;
using System.Threading.Tasks;

namespace CK.Build
{
    /// <summary>
    /// Extends a <see cref="IArtifactFeedIdentity"/> to provide capability to
    /// obtain <see cref="IPackageInfo"/> from <see cref="ArtifactInstance"/>.
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
        Task<IPackageInfo?> GetPackageInfoAsync( IActivityMonitor monitor, ArtifactInstance instance );
    }


}
