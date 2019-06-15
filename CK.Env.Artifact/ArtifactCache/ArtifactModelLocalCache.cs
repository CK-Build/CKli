using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env.ArtifactCache
{
    class ArtifactModelLocalCache
    {
        PackageDB _db;

        public ArtifactModelLocalCache()
        {
            _db = new PackageDB();
        }

        public PackageDB DB => _db;

        /// <summary>
        /// Gets the available instances in all the feeds.
        /// </summary>
        /// <param name="artifactName">The artifact name.</param>
        /// <returns>The available instances per feeds.</returns>
        public IReadOnlyCollection<ArtifactAvailableInstances> GetAvailableVersions( Artifact artifact )
        {
            return _db.Feeds.Where( f => f.ArtifactType == artifact.Type )
                         .Select( f => f.GetAvailableInstances( artifact.Name ) )
                         .Where( a => a.IsValid )
                         .ToList();
        }

    }
}
