using CK.Core;
using CSemVer;
using System;

namespace CK.Env
{
    /// <summary>
    /// Models a source of artifacts.
    /// </summary>
    public interface IArtifactSource
    {
        /// <summary>
        /// Identifies this source. Names should be unique (and may embedd the <see cref="ArtifactType.Name"/>)
        /// for display but are not used as keys: unicity is not a string requirment here.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the artifact type.
        /// </summary>
        ArtifactType ArtifactType { get; }

        /// <summary>
        /// Gets the most recent version for an artifact and a given quality available
        /// in this source.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="artifactName">The artifact name.</param>
        /// <param name="quality">The quality. By default the last one is returned whatever quality it is.</param>
        /// <returns>The best version or null if not found.</returns>
        SVersion GetCurrentVersion( IActivityMonitor m, string artifactName, PackageQuality quality = PackageQuality.CI );
    }
}
