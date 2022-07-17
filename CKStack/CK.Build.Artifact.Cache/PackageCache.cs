using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using CK.Build.PackageDB;
using System.IO;

namespace CK.Build.PackageDB
{

    /// <summary>
    /// Thread safe wrapper of an immutable <see cref="PackageDatabase"/> object.
    /// </summary>
    public class PackageCache
    {
        PackageDatabase _db;
        readonly object _lock;
        readonly NormalizedPath _path;
        int _lastSavedSerial;
        bool _autoSave;

        /// <summary>
        /// Initializes a new empty in memory only cache (cannot be saved).
        /// </summary>
        public PackageCache()
            : this( PackageDatabase.Empty, default, false )
        {
        }

        /// <summary>
        /// Loads or creates a new <see cref="PackageCache"/> bound to a file on the file system.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="path">The path that must be <see cref="NormalizedPath.IsRooted"/>.</param>
        /// <param name="autoSave">False to not save the cache automatically on changes.</param>
        /// <returns>The package cache.</returns>
        public static PackageCache LoadOrCreate( IActivityMonitor monitor, in NormalizedPath path, bool autoSave = true )
        {
            Throw.CheckNotNullArgument( monitor );
            Throw.CheckArgument( path.IsRooted );

            PackageDatabase? db = null;
            if( File.Exists( path ) )
            {
                try
                {
                    using var input = File.OpenRead( path );
                    using var r = new CKBinaryReader( input );
                    db =  new PackageDatabase( r );
                    monitor.Info( $"Package cache read from file '{path}' with {db.Instances.Count} packages in {db.Feeds.Count} feeds." );
                }
                catch( Exception ex )
                {
                    monitor.Error( "Unable to read package database. Reset it to an empty cache.", ex );
                    db = null;
                }
            }
            if( db == null )
            {
                monitor.Info( $"Initializing a new package cache (file '{path}')." );
                db = PackageDatabase.Empty;
            }
            return new PackageCache( db, path, autoSave );
        }

        PackageCache( PackageDatabase db, NormalizedPath path, bool autoSave )
        {
            _db = db;
            _path = path;
            _lock = new object();
            _lastSavedSerial = db.UpdateSerialNumber;
            _autoSave = autoSave;
        }

        /// <summary>
        /// Gets the file path of this package cache.
        /// <see cref="NormalizedPath.IsEmptyPath"/> if this is an in-memory only cache.
        /// </summary>
        public NormalizedPath Path => _path;

        /// <summary>
        /// Gets whether this cache is saved into <see cref="Path"/>
        /// on each change.
        /// This is ignored if this is an in-memory only cache.
        /// </summary>
        public bool AutoSave => _autoSave;

        /// <summary>
        /// Sets <see cref="AutoSave"/>, immediately calling <see cref="TrySave(IActivityMonitor)"/> if <paramref name="autoSave"/> is true.
        /// This is ignored if this is an in-memory only cache.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="autoSave">The <see cref="AutoSave"/> value.</param>
        public void SetAutoSave( IActivityMonitor monitor, bool autoSave )
        {
            if( autoSave ) TrySave( monitor );
            _autoSave = autoSave;
        }

        /// <summary>
        /// Tries to save this cache into <see cref="Path"/> if not already saved
        /// (this uses <see cref="PackageDatabase.UpdateSerialNumber"/>)
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false is an error occurred.</returns>
        public bool TrySave( IActivityMonitor monitor )
        {
            if( _db.UpdateSerialNumber != _lastSavedSerial )
            {
                lock( _lock )
                {
                    if( _db.UpdateSerialNumber != _lastSavedSerial )
                    {
                        return DoSave( monitor );
                    }
                }
            }
            return true;
        }

        bool DoSave( IActivityMonitor monitor )
        {
            Debug.Assert( Monitor.IsEntered( _lock ) );
            if( _path.IsEmptyPath )
            {
                monitor.Warn( "In-memory only cache cannot be saved." );
                return false;
            }
            using( monitor.OpenInfo( $"{(_autoSave ? "(Auto)":"")}Saving Package database: {_db} in '{_path}'." ) )
            {
                try
                {
                    using( var output = File.OpenWrite( _path ) )
                    {
                        _db.Write( new CKBinaryWriter( output ) );
                    }
                    _lastSavedSerial = _db.UpdateSerialNumber;
                    return true;
                }
                catch( Exception ex )
                {
                    monitor.Error( $"While saving {_db} in '{_path}'.", ex );
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets the package database.
        /// </summary>
        public PackageDatabase DB => _db;

        /// <summary>
        /// Fires whenever <see cref="DB"/> has changed.
        /// </summary>
        public event EventHandler<ChangedInfoEventArgs>? DBChanged;

        /// <summary>
        /// Drops a feed from the database.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="artifactType">The type of the feed to drop.</param>
        /// <param name="name">The name of the feed to drop.</param>
        /// <returns>The new database or null on error.</returns>
        public PackageDatabase? DropFeed( IActivityMonitor monitor, ArtifactType artifactType, string name ) => ApplyChanges( monitor, ( m, db ) => db.DropFeed( m, new Artifact( artifactType, name ) ) );

        /// <summary>
        /// Registers one package. Any <see cref="FullPackageInstanceInfo.Dependencies"/> must
        /// be already registered.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="info">The package to register.</param>
        /// <returns>The new database or null on error.</returns>
        public PackageDatabase? Add( IActivityMonitor monitor, IFullPackageInfo info ) => Add( monitor, new[] { info } );

        /// <summary>
        /// Registers multiple packages at once. Any <see cref="FullPackageInstanceInfo.Dependencies"/> must
        /// be already registered or appear before the dependent package in the <paramref name="infos"/> enumerable.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="infos">The package informations.</param>
        /// <returns>The new database or null on error.</returns>
        public PackageDatabase? Add( IActivityMonitor monitor, IEnumerable<IFullPackageInfo> infos ) => ApplyChanges( monitor, ( m, db ) => db.Add( m, infos ) );

        PackageDatabase? ApplyChanges( IActivityMonitor monitor, Func<IActivityMonitor,PackageDatabase,ChangedInfo?> action )
        {
            ChangedInfo? info;
            lock( _lock )
            {
                info = action( monitor, _db );
                if( info == null ) return null;
                Debug.Assert( info.HasChanged == (_db != info.DB) );
                if( !info.HasChanged ) return _db;
                _db = info.DB;
                if( _autoSave ) TrySave( monitor );
            }
            DBChanged?.Invoke( this, new ChangedInfoEventArgs( monitor, info ) );
            return info.DB;
        }
    }
}
