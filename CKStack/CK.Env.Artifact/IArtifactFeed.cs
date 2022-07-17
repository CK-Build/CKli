using CK.Core;
using CK.Build;
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
        /// Must configure any required credentials (or does nothing if no credentials are needed).
        /// This must throw if credentials cannot be settled.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        void ConfigureCredentials( IActivityMonitor monitor );

        /// <summary>
        /// Gets the best set of versions of an artifact in this feed.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="artifactName">The artifact name.</param>
        /// <returns>The set of instances or null on error.</returns>
        Task<ArtifactAvailableInstances?> GetVersionsAsync( IActivityMonitor monitor, string artifactName );

    }
}
