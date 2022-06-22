using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using CK.Build.PackageDB;

namespace CK.Build.PackageDB
{

    /// <summary>
    /// Wraps the immutable <see cref="PackageDatabase"/> object.
    /// </summary>
    public class PackageCache
    {
        PackageDatabase _db;
        readonly object _lock;

        /// <summary>
        /// Initializes a new empty cache.
        /// </summary>
        public PackageCache()
        {
            _db = PackageDatabase.Empty;
            _lock = new object();
        }

        /// <summary>
        /// Reads this cache from a serialized data.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="reader">The deserializer to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Read( IActivityMonitor monitor, ICKBinaryReader reader )
        {
            Throw.CheckNotNullArgument( reader );
            try
            {
                DeserializerContext ctx = new DeserializerContext( reader );
                lock( _lock )
                {
                    _db = new PackageDatabase( ctx );
                }
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
        public PackageDatabase DB => _db;

        /// <summary>
        /// Fires whenever <see cref="DB"/> has changed.
        /// </summary>
        public event EventHandler<ChangedInfo>? DBChanged;

        /// <summary>
        /// Registers one package. Any <see cref="FullPackageInstanceInfo.Dependencies"/> must
        /// be already registered.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="info">The package to register.</param>
        /// <param name="skipExisting">
        /// False to log an error and return null if info is already registered.
        /// </param>
        /// <returns>The new database or null on error.</returns>
        public PackageDatabase? Add( IActivityMonitor m, IFullPackageInfo info ) => Add( m, new[] { info } );

        /// <summary>
        /// Registers multiple packages at once. Any <see cref="FullPackageInstanceInfo.Dependencies"/> must
        /// be already registered or appear before the dependent package in the <paramref name="infos"/> enumerable.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="infos">The package informations.</param>
        /// <returns>The new database or null on error.</returns>
        public PackageDatabase? Add( IActivityMonitor m, IEnumerable<IFullPackageInfo> infos )
        {
            ChangedInfo? info;
            lock( _lock )
            {
                info = _db.Add( m, infos );
                if( info == null ) return null;
                Debug.Assert( info.HasChanged == (_db != info.DB) );
                if( !info.HasChanged ) return _db;
                _db = info.DB;
            }
            DBChanged?.Invoke( this, info );
            return info.DB;
        }
    }
}
