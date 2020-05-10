using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Core
{
    public partial class PackageDB
    {
        readonly InstanceStore _instances;
        readonly Dictionary<string, PackageFeed> _feeds;
        readonly DateTime _lastUpdate;
        readonly int _version;

        /// <summary>
        /// Initializes a new, empty, database.
        /// </summary>
        public PackageDB()
        {
            _instances = new InstanceStore();
            _feeds = new Dictionary<string, PackageFeed>();
            _lastUpdate = DateTime.UtcNow;
        }

        /// <summary>
        /// Initializes a database from its serialized binary data.
        /// </summary>
        /// <param name="reader">The reader to use.</param>
        public PackageDB( ICKBinaryReader reader )
            : this( new DeserializerContext( reader ) )
        {
        }

        /// <summary>
        /// Writes this database into a binary stream.
        /// </summary>
        /// <param name="ctx">The binary writer to use.</param>
        public void Write( ICKBinaryWriter writer ) => Write( new SerializerContext( writer ) );

        /// <summary>
        /// Initializes a database from its serialized binary data.
        /// </summary>
        /// <param name="ctx">The deserialization context to use.</param>
        internal PackageDB( DeserializerContext ctx )
        {
            _instances = new InstanceStore( ctx );
            int nbFeeds = ctx.Reader.ReadNonNegativeSmallInt32();
            _feeds = new Dictionary<string, PackageFeed>( nbFeeds );
            while( --nbFeeds >= 0 )
            {
                var f = new PackageFeed( _instances, ctx );
                _feeds.Add( f.TypedName, f );
            }
            _lastUpdate = DateTime.UtcNow;
        }

        /// <summary>
        /// Writes this database into a binary stream.
        /// </summary>
        /// <param name="ctx">The serialization context to use.</param>
        internal void Write( SerializerContext ctx )
        {
            _instances.Write( ctx );
            ctx.Writer.WriteNonNegativeSmallInt32( _feeds.Count );
            foreach( var kv in _feeds )
            {
                kv.Value.Write( _instances, ctx );
            }
            ctx.Writer.Write( _lastUpdate );
        }

        PackageDB( PackageDB origin, InstanceStore? store, Dictionary<string, PackageFeed>? newFeeds, DateTime lastUpdate )
        {
            _version = origin._version + 1;
            _feeds = newFeeds ?? origin._feeds;
            _instances = store ?? origin._instances;
            _lastUpdate = lastUpdate;
        }

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
        public PackageDB? Add( IActivityMonitor m, FullPackageInfo info, bool skipExisting = true )
        {
            return Add( m, new[] { info }, skipExisting );
        }

        /// <summary>
        /// Registers multiple packages at once. Any <see cref="FullPackageInfo.Dependencies"/> must
        /// be already registered or appear before the dependent package.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="infos">The package informations.</param>
        /// <param name="skipExisting">
        /// False to log an error and return null if infos contains already registered packages.
        /// By default, existing packages are silently ignored.
        /// </param>
        /// <returns>The new database (or this one if nothing changed), or null on error.</returns>
        public PackageDB? Add( IActivityMonitor m, IEnumerable<FullPackageInfo> infos, bool skipExisting = true )
        {
            DateTime regDate = DateTime.UtcNow;
            (FullPackageInfo info, Artifact[]? feedNames, int idx, PackageInstance? p)[] initialization;
            initialization = infos.Select( i => (i, i.CheckValidAndParseFeedNames( m ), ~_instances.IndexOf( i.Key ), (PackageInstance?)null) )
                                  .ToArray();
            int newCount = 0;
            Dictionary<string, PackageFeed>? newFeeds = null;
            List<(PackageFeed feed, List<PackageInstance> newPackages)>? feedPackages = null;
            for( int i = 0; i < initialization.Length; ++i )
            {
                var init = initialization[i];
                // CheckValidAndParseFeedNames returned a null feedNames array if anything was not valid.
                if( init.feedNames == null ) return null;
                Debug.Assert( init.info.Key.IsValid && init.feedNames.All( f => f.IsValid ) );
                if( init.idx < 0 )
                {
                    if( !skipExisting )
                    {
                        m.Error( $"Package {init.info.Key} is already registered." );
                        return null;
                    }
                }
                else
                {
                    var targets = init.info.Dependencies
                                           .Select( d => (d.Target,
                                                           _instances.Find( d.Target )
                                                           ?? initialization
                                                                .Take( i )
                                                                .Select( t => t.p )
                                                                .FirstOrDefault( p => p != null
                                                                                 && p.Key == d.Target )) )
                                           .ToArray();
                    if( targets.Any( t => t.Item2 == null ) )
                    {
                        m.Error( $"Dependency Target(s) of {init.info.Key} not registered: {targets.Where( t => t.Item2 == null ).Select( t => t.Target.ToString() ).Concatenate()}" );
                        return null;
                    }
                    var deps = init.info.Dependencies.Zip( targets, ( d, t ) => new PackageInstance.Reference( t.Item2!, d.Kind, d.Savors ) )
                                   .ToArray();
                    var newOne = new PackageInstance( init.info.Key, init.info.Savors, deps, regDate );
                    initialization[i].p = newOne;
                    ++newCount;
                    if( init.feedNames.Length > 0 )
                    {
                        if( newFeeds == null ) newFeeds = new Dictionary<string, PackageFeed>( _feeds );
                        foreach( var name in init.feedNames )
                        {
                            var keyName = name.TypedName;
                            if( !_feeds.TryGetValue( keyName, out var feed )
                                && !newFeeds.TryGetValue( keyName, out feed ) )
                            {
                                newFeeds.Add( keyName, new PackageFeed( name, new InstanceStore( newOne ) ) );
                            }
                            else
                            {
                                if( feedPackages == null ) feedPackages = new List<(PackageFeed feed, List<PackageInstance> newPackages)>();
                                int idx = feedPackages.IndexOf( t => t.feed == feed );
                                if( idx < 0 ) feedPackages.Add( (feed, new List<PackageInstance>() { newOne }) );
                                else feedPackages[idx].newPackages.Add( newOne );
                            }
                        }
                    }
                }
            }
            if( newCount == 0 ) return this;
            if( feedPackages != null )
            {
                Debug.Assert( newFeeds != null );
                foreach( var (feed, newPackages) in feedPackages )
                {
                    newFeeds[feed.TypedName] = new PackageFeed( feed, newPackages );
                }
            }
            var indices = initialization.Where( x => x.p != null ).Select( x => (x.idx, x.p!) ).ToArray();
            return new PackageDB( this, _instances.Add( indices ), newFeeds, regDate );
        }

        /// <summary>
        /// Gets the last update time.
        /// </summary>
        public DateTime LastUpdate => _lastUpdate;

        /// <summary>
        /// Returns this database with a new <see cref="LastUpdate"/> time.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public PackageDB WithLastUpdate( DateTime t )
        {
            return t != _lastUpdate ? new PackageDB( this, null, null, t ) : this;
        }

        /// <summary>
        /// Gets the version number.
        /// It is an always increasing number (that may roll to negative... in years).
        /// </summary>
        public int Version => _version;

        /// <summary>
        /// Gets all the feeds.
        /// </summary>
        public IEnumerable<PackageFeed> Feeds => _feeds.Values;

        /// <summary>
        /// Finds a feed by its <see cref="IArtifactFeedIdentity.TypedName"/>.
        /// </summary>
        /// <param name="typedName">The type name.</param>
        /// <returns>The feed or null.</returns>
        public PackageFeed? FindFeed( string typedName ) => _feeds.GetValueOrDefault( typedName );

        /// <summary>
        /// Gets the whole list of known packages.
        /// </summary>
        public IReadOnlyList<PackageInstance> Instances => _instances;

        /// <summary>
        /// Gets the instance or null if not found.
        /// </summary>
        /// <param name="key">The package identifier.</param>
        /// <returns>The instance or null if not found.</returns>
        public PackageInstance? Find( in ArtifactInstance key ) => _instances.Find( key );


        /// <summary>
        /// Gets all the instances of a given type.
        /// </summary>
        /// <param name="type">The package's type.</param>
        /// <returns>The list of the known instances.</returns>
        public IReadOnlyList<PackageInstance> GetInstances( ArtifactType type )
        {
            return _instances.GetInstances( type );
        }

        /// <summary>
        /// Gets all the instances of a package.
        /// </summary>
        /// <param name="package">The package.</param>
        /// <returns>The list of the known instances.</returns>
        public IReadOnlyList<PackageInstance> GetInstances( Artifact package )
        {
            return _instances.GetInstances( package );
        }

    }
}
