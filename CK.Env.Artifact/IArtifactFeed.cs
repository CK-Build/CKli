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
    public interface IArtifactFeed
    {
        /// <summary>
        /// Identifies this feed. This starts with the <see cref="ArtifactType.Name"/> and must uniquely
        /// identify this feed.
        /// </summary>
        string TypedName { get; }

        /// <summary>
        /// Gets the artifact type that this feed supports.
        /// </summary>
        ArtifactType ArtifactType { get; }

        /// <summary>
        /// Gets the best set of versions of a set of artifacts in this feed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="artifactNames">The artifact names.</param>
        /// <returns>The set of instances.</returns>
        Task<IReadOnlyCollection<ArtifactAvailableInstances>> GetVersionsAsync( IActivityMonitor m, IEnumerable<string> artifactNames );

    }
}
