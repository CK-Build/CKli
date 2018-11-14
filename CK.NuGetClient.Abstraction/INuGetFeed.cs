using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.NuGetClient
{
    /// <summary>
    /// 
    /// </summary>
    public interface INuGetFeed
    {
        /// <summary>
        /// Gets the info of this feed.
        /// </summary>
        INuGetFeedInfo Info { get; }

        /// <summary>
        /// Must provide the secret key name.
        /// </summary>
        string SecretKeyName { get; }

        /// <summary>
        /// Ensures that the secret behind the <see cref="SecretKeyName"/> is available.
        /// The implementation must ensure that the secret only depends from the <see cref="SecretKeyName"/>:
        /// if two feeds share the same SecretKeyName, the resolved secret must be the same.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The non empty secret or null.</returns>
        string ResolveSecret( IActivityMonitor m, bool throwOnEmpty = false );

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
        Task PushPackagesAsync( IActivityMonitor m, IEnumerable<LocalNuGetPackageFile> files, int timeoutSeconds = 20 );
    }
}
