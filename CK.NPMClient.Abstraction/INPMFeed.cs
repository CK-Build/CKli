using CK.Core;
using CK.Env;
using CSemVer;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CK.NPMClient
{
    /// <summary>
    /// Defines a NPM feed, either remote or local.
    /// </summary>
    public interface INPMFeed : IArtifactRepository
    {
        /// <summary>
        /// Gets the info of this feed.
        /// </summary>
        new INPMFeedInfo Info { get; }

        /// <summary>
        /// Cheks whether a versioned package exists in this feed.
        /// </summary>
        /// <param name="m">The activity monitor.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="v">The version.</param>
        /// <returns>True if found, false otherwise.</returns>
        Task<bool> ExistsAsync( IActivityMonitor m, string packageId, SVersion v );

        /// <summary>
        /// Pushes a set of packages.
        /// </summary>
        /// <param name="ctx">The monitor to use.</param>
        /// <param name="files">The set of packages to push.</param>
        /// <param name="timeoutSeconds">Timeout in seconds.</param>
        /// <returns>The awaitable.</returns>
        Task PushPackagesAsync( IActivityMonitor m, IEnumerable<LocalNPMPackageFile> files, int timeoutSeconds = 20 );
    }
}