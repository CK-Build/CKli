using CK.Core;
using CK.NuGetClient;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Handles the local feeds for master, develop and local branches.
    /// </summary>
    public interface IEnvLocalFeedProvider
    {
        /// <summary>
        /// Gets the local environment feed for the 3 standard branches.
        /// </summary>
        /// <param name="branch">The branch status.</param>
        /// <returns>The local feed info or null if it is not one of the 3 standard ones.</returns>
        IEnvLocalFeed GetFeed( StandardGitStatus branch );

        /// <summary>
        /// Gets the local feed.
        /// </summary>
        IEnvLocalFeed Local { get; }

        /// <summary>
        /// Gets the develop feed.
        /// </summary>
        IEnvLocalFeed Develop { get; }

        /// <summary>
        /// Gets the master feed.
        /// </summary>
        IEnvLocalFeed Master { get; }

        /// <summary>
        /// Removes a specific version of a package from the local NuGet cache.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package's version.</param>
        void RemoveFromNuGetCache( IActivityMonitor m, string packageId, SVersion version );

    }
}
