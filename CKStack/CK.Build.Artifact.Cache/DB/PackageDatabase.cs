using CK.Core;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;

namespace CK.Build.PackageDB
{
    public partial class PackageDatabase
    {
        readonly InstanceStore _instances;
        // The key is the Artifact.TypedName ($"{Type}:{Name}").
        // Since ArtifactType is case sensitive but Artifact.Name is case insensitive,
        // This dictionary must use StringComparer.OrdinalIgnoreCase.
        readonly Dictionary<string, PackageFeed> _feeds;
        readonly DateTime _lastUpdate;
        readonly int _updateSerialNumber;

        /// <summary>
        /// Gets the empty package database.
        /// </summary>
        public static readonly PackageDatabase Empty = new PackageDatabase();

        PackageDatabase()
        {
            _instances = InstanceStore.Empty;
            _feeds = new Dictionary<string, PackageFeed>( StringComparer.OrdinalIgnoreCase );
            _lastUpdate = Util.UtcMinValue;
        }

        /// <summary>
        /// Initializes a database from its serialized binary data.
        /// </summary>
        /// <param name="r">The reader to use.</param>
        public PackageDatabase( ICKBinaryReader r )
        {
            var ctx = new DeserializerContext( r );
            _instances = new InstanceStore( ctx );
            int nbFeeds = ctx.Reader.ReadNonNegativeSmallInt32();
            _feeds = new Dictionary<string, PackageFeed>( nbFeeds, StringComparer.OrdinalIgnoreCase );
            while( --nbFeeds >= 0 )
            {
                var f = new PackageFeed( _instances, ctx );
                _feeds.Add( f.TypedName, f );
            }
            _lastUpdate = r.ReadDateTime();
        }

        /// <summary>
        /// Writes this database into a binary stream.
        /// </summary>
        /// <param name="ctx">The binary writer to use.</param>
        public void Write( ICKBinaryWriter w )
        {
            var ctx = new SerializerContext( w );
            _instances.Write( ctx );
            ctx.Writer.WriteNonNegativeSmallInt32( _feeds.Count );
            foreach( var kv in _feeds )
            {
                kv.Value.Write( _instances, ctx );
            }
            ctx.Writer.Write( _lastUpdate );
        }

        PackageDatabase( PackageDatabase origin, InstanceStore? store, Dictionary<string, PackageFeed>? newFeeds, DateTime lastUpdate )
        {
            _updateSerialNumber = origin._updateSerialNumber + 1;
            _feeds = newFeeds ?? origin._feeds;
            _instances = store ?? origin._instances;
            _lastUpdate = lastUpdate;
        }

        /// <summary>
        /// Drops a feed if it exists.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="artifact">The feed to drop.</param>
        /// <returns></returns>
        public ChangedInfo? DropFeed( IActivityMonitor monitor, in Artifact artifact )
        {
            var typedName = artifact.TypedName;
            if( _feeds.TryGetValue( typedName, out var feed ) )
            {
                var newFeeds = new Dictionary<string, PackageFeed>( _feeds.Count - 1, StringComparer.OrdinalIgnoreCase );
                foreach( var f in _feeds )
                {
                    if( f.Key != typedName ) newFeeds.Add( f.Key, f.Value );
                }
                var db = new PackageDatabase( this, _instances, newFeeds, DateTime.UtcNow );
                return new ChangedInfo( db,
                                        true,
                                        Array.Empty<PackageChangedInfo>(),
                                        Array.Empty<PackageFeed>(),
                                        Array.Empty<FeedChangedInfo>(),
                                        droppedFeeds: new[] { feed } );
            }
            return new ChangedInfo( this );
        }


        /// <summary>
        /// Registers one package. Any <see cref="IPackageInstanceInfo.Dependencies"/> must
        /// be already registered.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="info">The package to register.</param>
        /// <returns>The new database or null on error.</returns>
        public ChangedInfo? Add( IActivityMonitor monitor, IFullPackageInfo info )
        {
            return Add( monitor, new[] { info } );
        }

        /// <summary>
        /// Registers multiple packages at once. Any <see cref="IPackageInstanceInfo.Dependencies"/> must
        /// be already registered (the <see cref="PackageInstance.Reference.BaseTargetKey"/> of the reference exists in the DB)
        /// or appear before the dependent package.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="infos">The package informations.</param>
        /// <returns>The change result withe the new database (or this one if nothing changed), or null on error.</returns>
        public ChangedInfo? Add( IActivityMonitor monitor, IEnumerable<IFullPackageInfo> infos )
        {
            (IFullPackageInfo info, Artifact[]? feedNames, int idx, PackageEventType status, PackageInstance? p)[] initialization;
            initialization = infos.Select( i => (i, i.CheckValidAndParseFeedNames( monitor ), ~_instances.IndexOf( i.Key ), PackageEventType.None, (PackageInstance?)null) )
                                  .ToArray();
            // Reusable buffers.
            var removeFeedList = new List<(PackageFeed, int)>( _feeds.Count );
            var feedToCheckList = new List<PackageFeed>( _feeds.Count );

            int newCount = 0;
            int updateCount = 0;
            // The new feeds contains an unordered package list.
            // We use an Artifact as the key here to capture the feed type and its name.
            // Note that the final _feeds's dictionary key is "type:name" string (the Artifact.TypedName).
            Dictionary<Artifact, List<PackageInstance>>? brandNewFeeds = null;
            // The packages to add to an existing feed or to remove from an existing feed.
            List<PackageFeed.Diff>? feedDiff = null;

            #region Processing the initialization array.
            for( int i = 0; i < initialization.Length; ++i )
            {
                ref var candidate = ref initialization[i];
                // CheckValidAndParseFeedNames returned a null feedNames array if anything was not valid.
                if( candidate.feedNames == null ) return null;
                Debug.Assert( candidate.info.Key.IsValid && candidate.feedNames.All( f => f.IsValid ) );

                removeFeedList.Clear();
                feedToCheckList.Clear();

                PackageEventType cs = PackageEventType.None;
                PackageInstance? pOld = null;
                PackageInstance? pNew = null;
                if( candidate.idx < 0 )
                {
                    // The package is already known.
                    pOld = _instances[~candidate.idx];
                    if( !candidate.info.CheckSameContent( pOld, monitor ) )
                    {
                        monitor.Warn( "Package content changed! Updating it anyway (but this should be investigated)." );
                        cs |= PackageEventType.ContentOnlyChanged;
                    }
                    if( candidate.info.State != pOld.State )
                    {
                        cs |= PackageEventType.StateOnlyChanged;
                    }
                    if( cs != PackageEventType.None )
                    {
                        // Something changed in the package: we need a new instance.
                        pNew = CreatePackageInstance( monitor, initialization, i, _instances );
                        if( pNew == null ) return null;
                        Debug.Assert( pNew.Key == candidate.info.Key && pOld.Key == pNew.Key );
                        ++updateCount;
                    }
                    // We populate the removeFeedList.
                    // This adds in the removeFeedList all the existing feeds of pOld.
                    // Below the candidate's FeedNames will be removed from this removedList
                    // (this is a diff).
                    // We cannot say here whether the feeds of the package have been modified
                    // or not.
                    if( candidate.info.AllFeedNamesAreKnown )
                    {
                        foreach( var f in _feeds.Values )
                        {
                            int idx = f.IndexOf( pOld.Key );
                            if( idx >= 0 )
                            {
                                // The package pOld exists in feed f.
                                removeFeedList.Add( (f, idx) );
                            }
                        }
                    }
                    else if( pNew != null )
                    {
                        // The package instance changed and the provided feed names are not
                        // the complete known set of feeds.
                        // We must update the instance in all feeds that reference the package.
                        feedToCheckList.AddRange( _feeds.Values );
                    }
                }
                else
                {
                    // This is a brand new package instance.
                    cs = PackageEventType.Added;
                    pNew = CreatePackageInstance( monitor, initialization, i, _instances );
                    if( pNew == null ) return null;
                    ++newCount;
                }
                // Now that package instance is handled, we have an instance that is either pNew or pOld.
                var p = pNew ?? pOld;
                Debug.Assert( p != null );

                // A package info may not define any feed.
                if( candidate.feedNames.Length > 0 )
                {
                    foreach( var name in candidate.feedNames )
                    {
                        if( name.Type != candidate.info.Key.Artifact.Type )
                        {
                            monitor.Error( $"Package {candidate.info.Key} cannot appear in feed {name}: Artifact's type differ." );
                            return null;
                        }
                        // Tests whether the feed name is a brand new one.
                        // In such case, creates the PackageFeed with pNew ?? pOld:
                        // there is nothing more to do here, the new feeds will filled with
                        // the appropriate instances.
                        if( !_feeds.TryGetValue( name.TypedName, out var feed ) )
                        {
                            if( brandNewFeeds == null ) brandNewFeeds = new Dictionary<Artifact, List<PackageInstance>>();
                            var f = brandNewFeeds.GetOrSet( name, _ => new List<PackageInstance>() );
                            f.Add( p );
                        }
                        else
                        {
                            // The feed exists.
                            // If it appears in the removeFeedList (because AllFeedNamesAreKnown),
                            // we can remove it from this removeFeedList.
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
                            feedToCheckList.Remove( feed );
                            // At the end of this loop on the feed names, the removeFeedList contains entries that
                            // must be removed since the package (necessarily an updated one otherwise the removeFeedList
                            // would be empty) must no more appear in these feeds.
                            if( !isFeedFound )
                            {
                                // The feed in not one of the removedFeedList. We must check whether
                                // the package must be added or updated.
                                var idxInFeed = feed.IndexOf( p.Key );
                                if( idxInFeed < 0 )
                                {
                                    // The package doesn't currently appear in the feed.
                                    // We add a positive index: its an add.
                                    AddOrUpdateInFeedDiff( ref feedDiff, p, feed, ~idxInFeed );
                                }
                                else
                                {
                                    // The package is already referenced. If we have a pNew, we must update it.
                                    if( pNew != null )
                                    {
                                        // We add a negative index: its an update.
                                        AddOrUpdateInFeedDiff( ref feedDiff, pNew, feed, ~idxInFeed );
                                    }
                                }
                            }
                        }
                    }
                }

                // If the removeFeedList is not empty, generate the diffs with the remove.
                // If the removeFeedList is empty, then we may have a non empty feedToCheckList: this
                // may generate diffs to update the updated package instances.
                Debug.Assert( (removeFeedList.Count > 0 && feedToCheckList.Count == 0)
                               || (removeFeedList.Count == 0 && feedToCheckList.Count > 0)
                               || (removeFeedList.Count == 0 && feedToCheckList.Count == 0) );
                if( removeFeedList.Count > 0 )
                {
                    if( feedDiff == null ) feedDiff = new List<PackageFeed.Diff>();
                    foreach( var (feed, idxPackage) in removeFeedList )
                    {
                        int idx = feedDiff.IndexOf( t => t.Feed == feed );
                        if( idx < 0 ) feedDiff.Add( new PackageFeed.Diff( feed, idxPackage ) );
                        else feedDiff[idx].Remove( idxPackage );
                    }
                }
                else if( feedToCheckList.Count > 0 )
                {
                    Debug.Assert( pOld != null && pNew != null );
                    foreach( var f in feedToCheckList )
                    {
                        int idxInFeed = f.IndexOf( pOld.Key );
                        if( idxInFeed >= 0 )
                        {
                            // We add a negative index: its an update.
                            AddOrUpdateInFeedDiff( ref feedDiff, pNew, f, ~idxInFeed );
                        }
                    }
                }
                // We memorize the change status.
                candidate.status = cs;
            }

            static PackageInstance? CreatePackageInstance( IActivityMonitor monitor,
                                                           (IFullPackageInfo info, Artifact[]? feedNames, int idx, PackageEventType status, PackageInstance? p)[] initialization,
                                                           int i,
                                                           InstanceStore instances )
            {
                ref var candidate = ref initialization[i];
                // Resolves the base version target packages.
                var targets = candidate.info.Dependencies
                                        .Select( d => (d.Target,
                                                       // First lookup in the initialization array so that a an updated package is
                                                       // used: the fallback to instances.Find below will find the existing (old) instance.
                                                       initialization
                                                            .Take( i )
                                                            .Select( t => t.p )
                                                            .FirstOrDefault( p => p != null && p.Key == d.Target )
                                                         ?? instances.Find( d.Target )) )
                                        .ToArray();
                if( targets.Any( t => t.Item2 == null ) )
                {
                    monitor.Error( $"Dependency Target(s) of {candidate.info.Key} not registered: {targets.Where( t => t.Item2 == null ).Select( t => t.Target.ToString() ).Concatenate()}" );
                    return null;
                }
                var allSavors = candidate.info.Savors;
                var deps = candidate.info.Dependencies.Zip( targets,
                                                            ( d, t ) => new PackageInstance.Reference( t.Item2!, d.Lock, d.MinQuality, d.Kind, d.Savors == allSavors
                                                                                                                                                ? null
                                                                                                                                                : d.Savors ) )
                                                       .ToArray();
                return candidate.p = new PackageInstance( candidate.info.Key, candidate.info.Savors, candidate.info.State, deps );
            }

            static List<PackageFeed.Diff> AddOrUpdateInFeedDiff( ref List<PackageFeed.Diff>? feedDiff, PackageInstance p, PackageFeed feed, int idxInFeed )
            {
                if( feedDiff == null ) feedDiff = new List<PackageFeed.Diff>();
                int idx = feedDiff.IndexOf( t => t.Feed == feed );
                if( idx < 0 ) feedDiff.Add( new PackageFeed.Diff( feed, (idxInFeed, p) ) );
                else feedDiff[idx].AddOrUpdate( idxInFeed, p );
                return feedDiff;
            }
            #endregion

            // If theres is no package updated, no new package, no new fields and no diffs in any feed, we are done.
            if( updateCount == 0 && newCount == 0 && brandNewFeeds == null && feedDiff == null )
            {
                return new ChangedInfo( this );
            }

            // Computing the new feeds dictionary and the new feeds and feed changes info.
            FeedChangedInfo[]? feedChanges = null;
            PackageFeed[]? newFeeds = null;
            Dictionary<string, PackageFeed>? feeds = null;

            if( feedDiff != null || brandNewFeeds != null )
            {
                feeds = new Dictionary<string, PackageFeed>( _feeds, StringComparer.OrdinalIgnoreCase );
                if( feedDiff != null )
                {
                    feedChanges = new FeedChangedInfo[feedDiff.Count];
                    int i = 0;
                    foreach( var d in feedDiff )
                    {
                        FeedChangedInfo c = d.Create();
                        feeds[d.Feed.TypedName] = c.Feed;
                        feedChanges[i++] = c;
                    }
                }
                if( brandNewFeeds != null )
                {
                    newFeeds = new PackageFeed[brandNewFeeds.Count];
                    int i = 0;
                    foreach( var fContent in brandNewFeeds )
                    {
                        var f = new PackageFeed( fContent.Key, new InstanceStore( fContent.Value ) );
                        feeds.Add( fContent.Key.TypedName, f );
                        newFeeds[i++] = f;
                    }
                }
            }

            var packageChanges = new PackageChangedInfo[updateCount + newCount];
            var indices = new (int idx, PackageInstance p)[updateCount + newCount];
            int iP = 0;
            foreach( var entry in initialization )
            {
                if( entry.p != null )
                {
                    Debug.Assert( entry.status != PackageEventType.None && entry.status != PackageEventType.Destroyed );
                    packageChanges[iP] = new PackageChangedInfo( entry.status, entry.p );
                    indices[iP++] = (entry.idx, entry.p);
                }
            }
            Debug.Assert( indices.All( e => e.p != null ) );
            var db = new PackageDatabase( this, _instances.AddOrUpdate( indices, updateCount ), feeds, DateTime.UtcNow );

            return new ChangedInfo( db,
                                    true,
                                    packageChanges,
                                    newFeeds ?? Array.Empty<PackageFeed>(),
                                    feedChanges ?? Array.Empty<FeedChangedInfo>(),
                                    droppedFeeds: Array.Empty<PackageFeed>() );
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
        public PackageDatabase WithLastUpdate( DateTime t )
        {
            return t != _lastUpdate ? new PackageDatabase( this, null, null, t ) : this;
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
