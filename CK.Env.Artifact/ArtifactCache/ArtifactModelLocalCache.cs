using CK.Core;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.ArtifactCache
{
    /// <summary>
    /// Small wrapper around the immutable <see cref="PackageDB"/> object.
    /// </summary>
    class ArtifactModelLocalCache
    {
        PackageDB _db;

        /// <summary>
        /// Initializes a new empty cache.
        /// </summary>
        public ArtifactModelLocalCache()
        {
            _db = new PackageDB();
        }

        /// <summary>
        /// Writes this cache into a binary stream.
        /// </summary>
        /// <param name="w">The writer to use.</param>
        public void Write( CKBinaryWriter w )
        {
            w.Write( 0 ); // Version.
            _db.Write( w );
        }

        /// <summary>
        /// Gets the package database.
        /// </summary>
        public PackageDB DB => _db;

        /// <summary>
        /// Registers one package. Any <see cref="PackageInfo.Dependencies"/> must
        /// be already registered.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="info">The package to register.</param>
        /// <param name="skipExisting">
        /// False to log an error and return null if info is already registered.
        /// </param>
        /// <returns>The new database or null on error.</returns>
        public PackageDB Add( IActivityMonitor m, PackageInfo info, bool skipExisting = true ) => Add( m, new[] { info }, skipExisting );

        /// <summary>
        /// Registers multiple packages at once. Any <see cref="PackageInfo.Dependencies"/> must
        /// be already registered or appear before the dependent package in the <paramref name="infos"/> enumerable.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="infos">The package informations.</param>
        /// <param name="skipExisting">
        /// False to log an error and return null if infos contains already registered packages.
        /// By default, exisiting packages are silently ignored.
        /// </param>
        /// <returns>The new database or null on error.</returns>
        public PackageDB Add( IActivityMonitor m, IEnumerable<PackageInfo> infos, bool skipExisting = true )
        {
            var newDb = _db.Add( m, infos, skipExisting );
            return newDb != null ? (_db = newDb) : null;
        }

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
