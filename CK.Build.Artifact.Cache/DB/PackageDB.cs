using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace CK.Build
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
        /// Registers one package. Any <see cref="IPackageInfo.Dependencies"/> must
        /// be already registered.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="info">The package to register.</param>
        /// <param name="skipExisting">
        /// False to log an error and return null if info is already registered.
        /// </param>
        /// <returns>The new database or null on error.</returns>
        public PackageDB? Add( IActivityMonitor m, IFullPackageInfo info, bool skipExisting = true )
        {
            return Add( m, new[] { info }, skipExisting );
        }

        /// <summary>
        /// Registers multiple packages at once. Any <see cref="IPackageInfo.Dependencies"/> must
        /// be already registered (the <see cref="PackageInstance.Reference.BaseTargetKey"/> of the reference exists in the DB)
        /// or appear before the dependent package.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="infos">The package informations.</param>
        /// <param name="skipExisting">
        /// False to log an error and return null if infos contains already registered packages.
        /// By default, existing packages are silently ignored.
        /// </param>
        /// <returns>The new database (or this one if nothing changed), or null on error.</returns>
        public PackageDB? Add( IActivityMonitor m, IEnumerable<IFullPackageInfo> infos, bool skipExisting = true )
        {
            DateTime regDate = DateTime.UtcNow;
            (IFullPackageInfo info, Artifact[]? feedNames, int idx, PackageInstance? p)[] initialization;
            initialization = infos.Select( i => (i, i.CheckValidAndParseFeedNames( m ), ~_instances.IndexOf( i.Key ), (PackageInstance?)null) )
                                  .ToArray();
            var removeFeedList = new List<(PackageFeed,int)>( _feeds.Count );
            int newCount = 0;
            // The new feeds (if needed, it will be initially a copy of the current _feeds).
            Dictionary<string, PackageFeed>? newFeeds = null;
            // The packages to add to an existing or new feed or to remove from an existing feed.
            List<PackageFeed.Diff>? feedDiff = null;
            for( int i = 0; i < initialization.Length; ++i )
            {
                var candidate = initialization[i];
                // CheckValidAndParseFeedNames returned a null feedNames array if anything was not valid.
                if( candidate.feedNames == null ) return null;
                Debug.Assert( candidate.info.Key.IsValid && candidate.feedNames.All( f => f.IsValid ) );

                removeFeedList.Clear();
                PackageInstance p;
                if( candidate.idx < 0 )
                {
                    if( !skipExisting )
                    {
                        m.Error( $"Package {candidate.info.Key} is already registered." );
                        return null;
                    }
                    // The package is already known.
                    p = _instances[~candidate.idx];
                    // We populate the removeFeedList.
                    if( candidate.info.AllFeedNamesAreKnown )
                    {
                        foreach( var f in _feeds.Values )
                        {
                            int idx = f.IndexOf( p.Key );
                            if( idx >= 0 )
                            {
                                // The package p exists in feed f.
                                removeFeedList.Add( (f, idx) );
                            }
                        }
                    }
                }
                else
                {
                    var targets = candidate.info.Dependencies
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
                        m.Error( $"Dependency Target(s) of {candidate.info.Key} not registered: {targets.Where( t => t.Item2 == null ).Select( t => t.Target.ToString() ).Concatenate()}" );
                        return null;
                    }
                    var deps = candidate.info.Dependencies.Zip( targets, ( d, t ) => new PackageInstance.Reference( t.Item2!, d.Lock, d.MinQuality, d.Kind, d.Savors ) )
                                   .ToArray();                   
                    initialization[i].p = p = new PackageInstance( candidate.info.Key, candidate.info.Savors, deps, regDate );
                    ++newCount;
                }

                // A package may not appear in any feed.
                if( candidate.feedNames.Length > 0 )
                {
                    foreach( var name in candidate.feedNames )
                    {
                        if( name.Type != p.Key.Artifact.Type )
                        {
                            m.Error( $"Package {p.Key} cannot appear in feed {name}: Artifact's type differ." );
                            return null;
                        }
                        var keyName = name.TypedName;
                        if( !_feeds.TryGetValue( keyName, out var feed )
                            && (newFeeds == null || !newFeeds.TryGetValue( keyName, out feed )) )
                        {
                            if( newFeeds == null ) newFeeds = new Dictionary<string, PackageFeed>( _feeds );
                            newFeeds.Add( keyName, new PackageFeed( name, new InstanceStore( p ) ) );
                        }
                        else
                        {
                            bool isFeedFound = false;
                            for( int iR = 0; iR < removeFeedList.Count; ++iR )
                            {
                                if( removeFeedList[iR].Item1 == feed )
                                {
                                    removeFeedList.RemoveAt( iR );
                                    isFeedFound = true;
                                    break;
                                }
                            }
                            if( !isFeedFound )
                            {
                                var idxInFeed = feed.IndexOf( p.Key );
                                if( idxInFeed < 0 )
                                {
                                    if( feedDiff == null ) feedDiff = new List<PackageFeed.Diff>();
                                    int idx = feedDiff.IndexOf( t => t.Feed == feed );
                                    if( idx < 0 ) feedDiff.Add( new PackageFeed.Diff(feed, (~idxInFeed, p) ));
                                    else feedDiff[idx].AddNew( ~idxInFeed, p );
                                }
                            }
                        }
                    }
                }
                // Handle feed cleaning.
                if( removeFeedList.Count > 0 )
                {
                    if( feedDiff == null ) feedDiff = new List<PackageFeed.Diff>();
                    foreach( var (feed,idxPackage) in removeFeedList )
                    {
                        int idx = feedDiff.IndexOf( t => t.Feed == feed );
                        if( idx < 0 ) feedDiff.Add( new PackageFeed.Diff(feed, idxPackage));
                        else feedDiff[idx].AddOld( idxPackage );
                    }

                }
            }
            if( newCount == 0 && newFeeds == null && removeFeedList.Count == 0 ) return this;
            if( feedDiff != null )
            {
                if( newFeeds == null ) newFeeds = new Dictionary<string, PackageFeed>( _feeds );
                foreach( var d in feedDiff )
                {
                    newFeeds[d.Feed.TypedName] = d.Create();
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
