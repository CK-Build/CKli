using CK.Build.PackageDB;
using CK.Core;
using CSemVer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CK.Build.PackageDB
{

    /// <summary>
    /// Thread safe shell around a <see cref="PackageCache"/> that handles updates from package feeds.
    /// </summary>
    public class LivePackageCache
    {
        readonly PackageCache _cache;
        IPackageFeed[] _feeds;
        readonly Dictionary<ArtifactInstance, TaskCompletionSource<FullPackageInstanceInfo?>> _currentRequests;
        readonly Queue<IActivityMonitor> _requestMonitors;

        /// <summary>
        /// Initializes a new <see cref="LivePackageCache"/>.
        /// </summary>
        /// <param name="cache">The cache to update.</param>
        /// <param name="feeds">Optional initial set of feeds to consider.</param>
        public LivePackageCache( PackageCache cache, IEnumerable<IPackageFeed>? feeds = null )
        {
            _cache = cache;
            _feeds = feeds?.ToArray() ?? Array.Empty<IPackageFeed>();
            _currentRequests = new Dictionary<ArtifactInstance, TaskCompletionSource<FullPackageInstanceInfo?>>();
            _requestMonitors = new Queue<IActivityMonitor>();
        }

        /// <summary>
        /// Loads or creates a new <see cref="LivePackageCache"/> bound to a file on the file system.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="path">The path that must be <see cref="NormalizedPath.IsRooted"/>.</param>
        /// <param name="feeds">Optional initial set of feeds to consider.</param>
        /// <param name="autoSaveCache">False to not save the cache automatically on changes.</param>
        /// <returns>The live package cache.</returns>
        public static LivePackageCache LoadOrCreate( IActivityMonitor monitor, in NormalizedPath path, IEnumerable<IPackageFeed>? feeds = null, bool autoSaveCache = true )
        {
            var c = PackageCache.LoadOrCreate( monitor, in path, autoSaveCache );
            return new LivePackageCache( c, feeds );
        }

        /// <summary>
        /// Gets the package cache that is updated by this updater.
        /// </summary>
        public PackageCache Cache => _cache;

        /// <summary>
        /// Gets the feeds from which package informations are retrieved.
        /// This list is mutable: use <see cref="AddFeed"/> and <see cref="RemoveFeed"/> to change it.
        /// </summary>
        public IReadOnlyList<IPackageFeed> Feeds => _feeds;

        /// <summary>
        /// Adds a feed into the <see cref="Feeds"/>.
        /// Note that a feed will appear in the <see cref="Cache"/> only when at least one package that
        /// is from the feed will be added.
        /// </summary>
        /// <param name="f">The feed to add.</param>
        public void AddFeed( IPackageFeed f ) => Util.InterlockedAdd( ref _feeds, f );

        /// <summary>
        /// Removes a feed from the <see cref="Feeds"/>.
        /// This doesn't drop the feed in the cache: the feed and its existing packages
        /// are kept. Use <see cref="DropFeed(IActivityMonitor, IPackageFeed)"/> to also
        /// remove the feed information from the package cache.
        /// </summary>
        /// <param name="f">The feed to remove.</param>
        public void RemoveFeed( IPackageFeed f, bool dropFromCache )
        {
            Util.InterlockedRemove( ref _feeds, f );
        }

        /// <summary>
        /// Removes a feed from the <see cref="Feeds"/>.
        /// </summary>
        /// <param name="f">The feed to remove.</param>
        public void DropFeed( IActivityMonitor monitor, IPackageFeed f )
        {
            Util.InterlockedRemove( ref _feeds, f );
            Cache.DropFeed( monitor, f.ArtifactType, f.Name );
        }

        /// <summary>
        /// Clears the current cache of feed requests that triggered an error and/or the ones
        /// that were unable to resolve the package instance. 
        /// </summary>
        /// <param name="forgetErrors">True to clear all existing request errors.</param>
        /// <param name="forgetUnresolved">True to clear previously unresolved package instance lookup.</param>
        public void ClearFeedRequestCache( bool forgetErrors, bool forgetUnresolved )
        {
            if( forgetErrors || forgetUnresolved )
            {
                lock( _currentRequests )
                {
                    IEnumerable<KeyValuePair<ArtifactInstance, TaskCompletionSource<FullPackageInstanceInfo?>>> all = _currentRequests;
                    if( forgetErrors ) all = all.Where( kv => kv.Value.Task.IsFaulted );
                    if( forgetUnresolved ) all = all.Where( kv => kv.Value.Task.IsCompletedSuccessfully && kv.Value.Task.Result == null );
                    foreach( var k in all.Select( kv => kv.Key ).ToList() )
                    {
                        _currentRequests.Remove( k );
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to locate or add a <see cref="PackageInstance"/> and all its dependencies recursively (all
        /// its <see cref="PackageInstance.Reference.BaseTargetKey"/>) to the <see cref="Cache"/>.
        /// Any resolution error (a missing dependency or when all feeds access fail for a package) is an exception.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="instance">The instance to resolve.</param>
        /// <returns>Null if the package cannot be found in any of the <see cref="Feeds"/>.</returns>
        public async Task<PackageInstance?> EnsureAsync( IActivityMonitor monitor, ArtifactInstance instance )
        {
            // First, consider this cache (fast path).
            // If the instance is currently known as a Ghost, do as if it was not yet present.
            PackageInstance? p = _cache.DB.Find( instance, allowGhost: false );
            if( p != null ) return p;

            // Second, ask our feeds to locate the package.
            // If it cannot be found in any of our feeds or an exception is thrown here, then we give up.

            // Captures the _feeds array.
            var feeds = _feeds;
            FullPackageInstanceInfo? info = await ReadFullInfoAsync( monitor, instance, feeds );
            if( info == null ) return null;

            var allDeps = new List<FullPackageInstanceInfo>();
            try
            {
                // The info object will be appended to the allDeps list even if an exception is raised here.
                await ReadMissingDependenciesAsync( monitor, info, allDeps, feeds );
                var db = _cache.Add( monitor, allDeps );
                p = db?.Find( instance, allowGhost: false );
                if( p == null )
                {
                    Throw.Exception( $"Error while updating the package cache for '{instance}', ." );
                }
                return p;
            }
            finally
            {
                // Cleanup the current requests: removes only the successful
                // requests: errors are cached as well as unresolved (null).
                lock( _currentRequests )
                {
                    foreach( var i in allDeps )
                    {
                        if( i.FeedNames.Count > 0 ) _currentRequests.Remove( i.Key );
                    }
                }
            }
        }

        async Task DoAddAsync( IActivityMonitor monitor, ArtifactInstance instance, List<FullPackageInstanceInfo> allDeps, IPackageFeed[] feeds )
        {
            if( _cache.DB.Find( instance, allowGhost: false ) != null || allDeps.Any( p => p.Key == instance ) ) return;
            FullPackageInstanceInfo? info = await ReadFullInfoAsync( monitor, instance, feeds );
            if( info == null )
            {
                // Unable to get a package info for this instance.
                // Instantiating a Ghost package info.
                monitor.Warn( $"Unable to find package '{instance}' in feeds {feeds.Where( f => f.ArtifactType == instance.Artifact.Type ).Select( f => f.Name ).Concatenate()}. A (hopefully) temporary Ghost package is created." );
                info = new FullPackageInstanceInfo();
                info.State = PackageState.Ghost;
                info.Key = instance;
                info.FeedNames.AddRange( feeds.Select( x => x.TypedName ) );
            }
            await ReadMissingDependenciesAsync( monitor, info, allDeps, feeds );
        }

        async Task ReadMissingDependenciesAsync( IActivityMonitor monitor, FullPackageInstanceInfo info, List<FullPackageInstanceInfo> allDeps, IPackageFeed[] feeds )
        {
            // From a starting FullPackageInfo (necessarily not null), recursively crawls the dependencies and always
            // append the starting FullPackageInfo to the allDeps list.
            // On error (a dependency failed to be read, either not found - null - or an exception is being thrown), we use
            // the FullPackageInfo.FeedNames by clearing it: this invalidates the starting FullPackageInfo but we
            // nevertheless add it to the allDeps list so that we can skip it when clearing the request cache (failed resolution
            // only must be kept in cache).
            try
            {
                foreach( var d in info.Dependencies )
                {
                    await DoAddAsync( monitor, d.Target, allDeps, feeds );
                }
            }
            catch
            {
                info.FeedNames.Clear();
                throw;
            }
            finally
            {
                allDeps.Add( info );
            }
        }

        Task<FullPackageInstanceInfo?> ReadFullInfoAsync( IActivityMonitor monitor, ArtifactInstance instance, IPackageFeed[] feeds )
        {
            // We use a simple dictionary to handle request duplicates.
            // Here we lookup the list for a pending TaskCompletionSource.
            // If we don't find it, we add a new one and launch the work.
            // The cleanup of this list is done at the end of the root EnsureAsync call.
            IActivityMonitor? requestMonitor;
            TaskCompletionSource<FullPackageInstanceInfo?>? tcs = null;
            lock( _currentRequests )
            {
                if( _currentRequests.TryGetValue( instance, out tcs ) )
                {
                    return tcs.Task;
                }
                tcs = new TaskCompletionSource<FullPackageInstanceInfo?>();
                _currentRequests.Add( instance, tcs );
                requestMonitor = ObtainRequestMonitor( instance );
            }
            _ = DoReadFullInfoAsync( requestMonitor, instance, tcs, feeds );
            return tcs.Task;
        }

        IActivityMonitor ObtainRequestMonitor( ArtifactInstance instance )
        {
            // Obtain the request monitor in the _currentRequests lock.
            Debug.Assert( Monitor.IsEntered( _currentRequests ) );
            var topic = $"Getting package info for '{instance.Artifact.Name}/{instance.Version}'.";
            if( _requestMonitors.TryDequeue( out var monitor ) )
            {
                monitor.SetTopic( topic );
            }
            else monitor = new ActivityMonitor( topic );
            return monitor;
        }

        // This never throws. The TCS is resolved.
        async Task DoReadFullInfoAsync( IActivityMonitor monitor, ArtifactInstance instance, TaskCompletionSource<FullPackageInstanceInfo?> result, IPackageFeed[] feeds )
        {
            FullPackageInstanceInfo p = new FullPackageInstanceInfo();
            List<Exception>? feedExceptions = null;
            Exception? invalidSameError = null;
            foreach( var f in feeds )
            {
                if( f.ArtifactType == instance.Artifact.Type )
                {
                    IPackageInstanceInfo? info = null;
                    using( monitor.OpenInfo( $"Reading package info of '{instance.Artifact.Name}/{instance.Version}' from feed '{f.TypedName}'." ) )
                    {
                        try
                        {
                            info = await f.GetPackageInfoAsync( monitor, instance );
                        }
                        catch( Exception ex )
                        {
                            monitor.Error( "Adding error to feed exceptions: this error is captured and cached.", ex );
                            if( feedExceptions == null ) feedExceptions = new List<Exception>();
                            feedExceptions.Add( ex );
                        }
                    }
                    if( info != null )
                    {
                        if( p.FeedNames.Count == 0 )
                        {
                            p.Key = info.Key;
                            p.Savors = info.Savors;
                            p.Dependencies.AddRange( info.Dependencies );
                            p.FeedNames.Add( f.TypedName );
                        }
                        else
                        {
                            if( !p.CheckSameContent( info, monitor ) )
                            {
                                invalidSameError = new Exception( $"Package info for {p.Key} from feed '{p.FeedNames[0]}' differ from the one of '{f.TypedName}'. See previous log error for details." );
                                break;
                            }
                            p.FeedNames.Add( f.Name );
                        }
                    }
                }
            }
            // When there is no info at all, it can be because...
            if( p.FeedNames.Count == 0 )
            {
                // We received no exception: the package cannot be resolved in the available feeds. We return null (ie. Not Found).
                if( feedExceptions == null )
                {
                    result.SetResult( null );
                }
                else
                {
                    // There has been at least one error: we cannot conclude that the package doesn't exist: we raise the error(s).
                    result.SetException( feedExceptions.Count == 1 ? feedExceptions[0] : new AggregateException( feedExceptions ) );
                }
            }
            else
            {
                // We have at least one feed that has the package...
                if( invalidSameError != null )
                {
                    // If data is not consistent across feeds, this is serious!
                    result.SetException( invalidSameError );
                }
                else
                {
                    // If we have feedExceptions, we forget them (they are logged): we have a package
                    // description and that's the most important.
                    result.SetResult( p );
                }
            }
            // Release the request monitor in the _currentRequests lock.
            lock( _currentRequests )
            {
                _requestMonitors.Enqueue( monitor );
            }
        }
    }

}
