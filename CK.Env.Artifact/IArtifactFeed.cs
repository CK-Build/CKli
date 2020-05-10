using CK.Core;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Models a source of packages (packages are installable artifacts).
    /// </summary>
    public interface IArtifactFeed : IArtifactFeedIdentity
    {
        /// <summary>
        /// Ensures that any secret required to retrieve packages is available.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="throwOnMissing">True to throw a <see cref="MissingRequiredSecretException"/> instead of returning false.</param>
        /// <returns>True on success, false if any required secret or configuration is missing.</returns>
        bool CheckSecret( IActivityMonitor m, bool throwOnMissing = false );

        /// <summary>
        /// Gets the best set of versions of an artifact in this feed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="artifactName">The artifact name.</param>
        /// <returns>The set of instances.</returns>
        Task<ArtifactAvailableInstances> GetVersionsAsync( IActivityMonitor m, string artifactName );

    }
}
