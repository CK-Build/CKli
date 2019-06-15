using CK.Core;
using CSemVer;
using System;
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
        /// Gets the best set of versions of an artifact in this feed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="artifactName">The artifact name.</param>
        /// <returns>The set of instances.</returns>
        Task<ArtifactAvailableInstances> GetVersionsAsync( IActivityMonitor m, string artifactName );

    }
}
