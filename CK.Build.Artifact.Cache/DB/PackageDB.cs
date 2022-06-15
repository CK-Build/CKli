using CK.Core;

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
        readonly int _updateSerialNumber;

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
            _updateSerialNumber = origin._updateSerialNumber + 1;
            _feeds = newFeeds ?? origin._feeds;
            _instances = store ?? origin._instances;
            _lastUpdate = lastUpdate;
        }

        /// <summary>
        /// Registers one package. Any <see cref="IPackageInstanceInfo.Dependencies"/> must
        /// be already registered.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="info">The package to register.</param>
        /// <returns>The new database or null on error.</returns>
        public PackageDB? Add( IActivityMonitor m, IFullPackageInfo info )
        {
            return Add( m, new[] { info } );
        }


        [Flags]
        enum PackageEventStatus
        {
            None = 0,
            Content = 1,
            State = 2,
            Feeds = 4,
            NewPackage = 8
        }

        /// <summary>
        /// Registers multiple packages at once. Any <see cref="IPackageInstanceInfo.Dependencies"/> must
        /// be already registered (the <see cref="PackageInstance.Reference.BaseTargetKey"/> of the reference exists in the DB)
        /// or appear before the dependent package.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="infos">The package informations.</param>
        /// <returns>The new database (or this one if nothing changed), or null on error.</returns>
        public PackageDB? Add( IActivityMonitor monitor, IEnumerable<IFullPackageInfo> infos )
        {
            DateTime regDate = DateTime.UtcNow;
            (IFullPackageInfo info, Artifact[]? feedNames, int idx, PackageEventStatus status, PackageInstance? p)[] initialization;
            initialization = infos.Select( i => (i, i.CheckValidAndParseFeedNames( monitor ), ~_instances.IndexOf( i.Key ), PackageEventStatus.None, (PackageInstance?)null) )
                                  .ToArray();
            var removeFeedList = new List<(PackageFeed,int)>( _feeds.Count );
            int newCount = 0;
            int changedCount = 0;
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

                PackageEventStatus cs = PackageEventStatus.None;
                PackageInstance? p = null;
                if( candidate.idx < 0 )
                {
                    // The package is already known.
                    p = _instances[~candidate.idx];
                    if( !candidate.info.CheckSameContent( p, monitor ) )
                    {
                        monitor.Warn( "Package content changed! Updating it anyway (but this should be investigated)." );
                        cs |= PackageEventStatus.Content;
                    }
                    if( candidate.info.State != p.State )
                    {
                        cs |= PackageEventStatus.State;
                    }
                    // We populate the removeFeedList.
                    // This adds in the removeFeedList all the existing feeds of p.
                    // Below the candidate.info.Feeds will be removed from this removedList
                    // (this is a diff).
                    // We cannot say here whether the feeds of the package have been modified
                    // or not.
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
                    // This is a brand new package instance.
                    cs = PackageEventStatus.NewPackage;
                    p = CreatePackageInstance( monitor, initialization, i, _instances );
                    if( p == null ) return null;
                    ++newCount;
                }

                // A package may not appear in any feed.
                if( candidate.feedNames.Length > 0 )
                {
                    foreach( var name in candidate.feedNames )
                    {
                        if( name.Type != candidate.info.Key.Artifact.Type )
                        {
                            monitor.Error( $"Package {candidate.info.Key} cannot appear in feed {name}: Artifact's type differ." );
                            return null;
                        }
                        var keyName = name.TypedName;
                        if( !_feeds.TryGetValue( keyName, out var feed )
                            && (newFeeds == null || !newFeeds.TryGetValue( keyName, out feed )) )
                        {
                            cs |= PackageEventStatus.Feeds;
                            if( newFeeds == null ) newFeeds = new Dictionary<string, PackageFeed>( _feeds );
                            p ??= CreatePackageInstance( monitor, initialization, i, _instances );
                            if( p == null ) return null;
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
                                var idxInFeed = feed.IndexOf( candidate.info.Key );
                                if( idxInFeed < 0 )
                                {
                                    if( feedDiff == null ) feedDiff = new List<PackageFeed.Diff>();
                                    int idx = feedDiff.IndexOf( t => t.Feed == feed );
                                    cs |= PackageEventStatus.Feeds;
                                    p ??= CreatePackageInstance( monitor, initialization, i, _instances );
                                    if( p == null ) return null;
                                    if( idx < 0 ) feedDiff.Add( new PackageFeed.Diff( feed, (~idxInFeed, p) ) );
                                    else feedDiff[idx].AddNew( ~idxInFeed, p );
                                }
                            }
                        }
                    }
                }
                // Handle feed cleaning.
                if( removeFeedList.Count > 0 )
                {
                    cs |= PackageEventStatus.Feeds;
                    if( feedDiff == null ) feedDiff = new List<PackageFeed.Diff>();
                    foreach( var (feed,idxPackage) in removeFeedList )
                    {
                        int idx = feedDiff.IndexOf( t => t.Feed == feed );
                        if( idx < 0 ) feedDiff.Add( new PackageFeed.Diff(feed, idxPackage));
                        else feedDiff[idx].AddOld( idxPackage );
                    }
                }
                if( cs != PackageEventStatus.None )
                {
                    initialization[i].status = cs;
                    if( cs != PackageEventStatus.NewPackage ) ++changedCount;
                }
            }

            if( changedCount == 0 && newCount == 0 && newFeeds == null && removeFeedList.Count == 0 ) return this;
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


            static PackageInstance? CreatePackageInstance( IActivityMonitor monitor,
                                                           (IFullPackageInfo info, Artifact[]? feedNames, int idx, PackageEventStatus status, PackageInstance? p)[] initialization,
                                                           int i,
                                                           InstanceStore instances )
            {
                ref var candidate = ref initialization[i];
                var targets = candidate.info.Dependencies
                                        .Select( d => (d.Target,
                                                        instances.Find( d.Target )
                                                        ?? initialization
                                                            .Take( i )
                                                            .Select( t => t.p )
                                                            .FirstOrDefault( p => p != null
                                                                                    && p.Key == d.Target )) )
                                        .ToArray();
                if( targets.Any( t => t.Item2 == null ) )
                {
                    monitor.Error( $"Dependency Target(s) of {candidate.info.Key} not registered: {targets.Where( t => t.Item2 == null ).Select( t => t.Target.ToString() ).Concatenate()}" );
                    return null;
                }
                var allSavors = candidate.info.Savors;
                var deps = candidate.info.Dependencies.Zip( targets, ( d, t ) => new PackageInstance.Reference( t.Item2!, d.Lock, d.MinQuality, d.Kind, d.Savors == allSavors ? null : d.Savors ) )
                                .ToArray();
                return candidate.p = new PackageInstance( candidate.info.Key, candidate.info.Savors, candidate.info.State, deps );
            }

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
        /// Gets the update serial number.
        /// It is an always increasing number (that may roll to negative... in years).
        /// </summary>
        public int UpdateSerialNumber => _updateSerialNumber;

        /// <summary>
        /// Gets all the feeds.
        /// </summary>
        public IReadOnlyCollection<PackageFeed> Feeds => _feeds.Values;

        /// <summary>
        /// Gets the available instances in all the feeds.
        /// Use <see cref="PackageFeed.GetAvailableInstances(string)"/> to retrieve the packages from one feed.
        /// </summary>
        /// <param name="artifactName">The artifact name.</param>
        /// <returns>The available instances per feeds.</returns>
        public IReadOnlyCollection<ArtifactAvailableInstances> GetAvailableVersions( Artifact artifact )
        {
            return _feeds.Values.Where( f => f.ArtifactType == artifact.Type )
                                .Select( f => f.GetAvailableInstances( artifact.Name ) )
                                .Where( a => a.IsValid )
                                .ToList();
        }

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
        /// <param name="allowGhost">True to allow returning Ghost instance.</param>
        /// <returns>The instance or null if not found.</returns>
        public PackageInstance? Find( in ArtifactInstance key, bool allowGhost = false )
        {
            var a = _instances.Find( key );
            return allowGhost || (a != null && !a.State.IsGhost()) ? a : null;
        }

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

        /// <summary>
        /// Overridden to return the <see cref="UpdateSerialNumber"/>, <see cref="Instances"/> count and the list of feeds.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"UpdateSerialNumber: {_updateSerialNumber}, PackageCount: {_instances.Count}, Feeds: {Feeds.Select( f => f.ToString() ).Concatenate()}";

    }
}
