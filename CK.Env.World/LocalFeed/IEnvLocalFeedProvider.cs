using CK.Core;
using CK.NuGetClient;
using CK.Text;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Handles the local feeds for ci builds (from 'develop' branch), releases (from 'master' and 'develop' branches),
    /// local builds (from 'local' branch) and Zero version builds.
    /// </summary>
    public interface IEnvLocalFeedProvider
    {
        /// <summary>
        /// Gets the local feed.
        /// </summary>
        IEnvLocalFeed Local { get; }

        /// <summary>
        /// Gets the CI feed.
        /// CI builds packages come here.
        /// </summary>
        IEnvLocalFeed CI { get; }

        /// <summary>
        /// Gets the Release feed.
        /// </summary>
        IEnvLocalFeed Release { get; }

        /// <summary>
        /// Gets the Zero builds feed.
        /// </summary>
        IEnvLocalFeed ZeroBuild { get; }

        /// <summary>
        /// Removes a specific version of a package from the local NuGet cache.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package's version.</param>
        void RemoveFromNuGetCache( IActivityMonitor m, string packageId, SVersion version );

        /// <summary>
        /// Checks whether a package exists in the NuGet cache folder.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package's version.</param>
        /// <returns>True if found, false otherwise.</returns>
        bool ExistsInNuGetCache( IActivityMonitor m, string packageId, SVersion version );

    }
}
