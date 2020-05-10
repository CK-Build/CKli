using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Core
{
    /// <summary>
    /// Small wrapper around the immutable <see cref="PackageDB"/> object.
    /// This exposes a centralized 
    /// </summary>
    public class ArtifactCache
    {
        PackageDB _db;

        /// <summary>
        /// Initializes a new empty cache.
        /// </summary>
        public ArtifactCache()
        {
            _db = new PackageDB();
        }

        /// <summary>
        /// Reads this cache from a serialized data.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="reader">The deserializer to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Read( IActivityMonitor monitor, ICKBinaryReader reader )
        {
            if( reader == null ) throw new ArgumentNullException( nameof( reader ) );
            try
            {
                DeserializerContext ctx = new DeserializerContext( reader );
                _db = new PackageDB( ctx );
                return true;
            }
            catch( Exception ex )
            {
                monitor.Error( "Unable to read package database.", ex );
                return false;
            }
        }

        /// <summary>
        /// Writes this cache into a binary stream.
        /// </summary>
        /// <param name="w">The writer to use.</param>
        public void Write( CKBinaryWriter w )
        {
            SerializerContext ctx = new SerializerContext( w );
            _db.Write( ctx );
        }

        /// <summary>
        /// Gets the package database.
        /// </summary>
        public PackageDB DB => _db;

        /// <summary>
        /// Fires whenever <see cref="DB"/> has changed.
        /// </summary>
        public event EventHandler? DBChanged;

        /// <summary>
        /// Registers one package. Any <see cref="FullPackageInfo.Dependencies"/> must
        /// be already registered.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="info">The package to register.</param>
        /// <param name="skipExisting">
        /// False to log an error and return null if info is already registered.
        /// </param>
        /// <returns>The new database or null on error.</returns>
        public PackageDB? Add( IActivityMonitor m, FullPackageInfo info, bool skipExisting = true ) => Add( m, new[] { info }, skipExisting );

        /// <summary>
        /// Registers multiple packages at once. Any <see cref="FullPackageInfo.Dependencies"/> must
        /// be already registered or appear before the dependent package in the <paramref name="infos"/> enumerable.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="infos">The package informations.</param>
        /// <param name="skipExisting">
        /// False to log an error and return null if infos contains already registered packages.
        /// By default, existing packages are silently ignored.
        /// </param>
        /// <returns>The new database or null on error.</returns>
        public PackageDB? Add( IActivityMonitor m, IEnumerable<FullPackageInfo> infos, bool skipExisting = true )
        {
            var newDb = _db.Add( m, infos, skipExisting );
            if( newDb == null ) return null;
            if( newDb != _db )
            {
                _db = newDb;
                DBChanged?.Invoke( this, EventArgs.Empty );
            }
            return _db;
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
