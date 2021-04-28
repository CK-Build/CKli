using CK.Core;
using CK.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CK.Build
{

    /// <summary>
    /// Shell around a <see cref="PackageCache"/> that handles updates from package feeds.
    /// </summary>
    public class LivePackageCache
    {
        readonly PackageCache _cache;
        IPackageFeed[] _feeds;
        readonly Dictionary<ArtifactInstance, TaskCompletionSource<FullPackageInfo?>> _currentRequests;
        readonly object _addLock;

        /// <summary>
        /// Initializes a new <see cref="LivePackageCache"/>.
        /// </summary>
        /// <param name="cache">The cache to update.</param>
        /// <param name="feeds">Optional initial set of feeds to consider.</param>
        public LivePackageCache( PackageCache cache, IEnumerable<IPackageFeed>? feeds = null )
        {
            _cache = cache;
            _feeds = feeds?.ToArray() ?? Array.Empty<IPackageFeed>();
            _currentRequests = new Dictionary<ArtifactInstance, TaskCompletionSource<FullPackageInfo?>>();
            _addLock = new object();
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
        /// </summary>
        /// <param name="f">The feed to add.</param>
        public void AddFeed( IPackageFeed f ) => Util.InterlockedAdd( ref _feeds, f );

        /// <summary>
        /// Removes a feed from the <see cref="Feeds"/>.
        /// </summary>
        /// <param name="f">The feed to remove.</param>
        public void RemoveFeed( IPackageFeed f ) => Util.InterlockedRemove( ref _feeds, f );

        /// <summary>
        /// Clears the current cache of feed requests that triggered an error and/or the ones
        /// that were unable to resolve the package instance. 
        /// </summary>
        /// <param name="forgetErrors">True to clear all existing request errors.</param>
        /// <param name="forgetUnresolved">True to clear previsously unresolved package instance lookup.</param>
        public void ClearFeedRequestCache( bool forgetErrors, bool forgetUnresolved )
        {
            if( forgetErrors || forgetUnresolved )
            {
                lock( _currentRequests )
                {
                    IEnumerable<KeyValuePair<ArtifactInstance, TaskCompletionSource<FullPackageInfo?>>> all = _currentRequests;
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
            PackageInstance? p = null;

            // First, consider this cache (fast path).
            p = _cache.DB.Find( instance );
            if( p != null ) return p;

            // Second, ask our feeds to locate the package.
            // If it cannot be found in any of our feeds or an exception is thrown here, then we give up.

            // Captures the _feeds array.
            var feeds = _feeds;
            FullPackageInfo? info = await ReadFullInfoAsync( monitor, instance, feeds );
            if( info == null ) return null;

            var allDeps = new List<FullPackageInfo>();
            try
            {
                // The info object will be appended to the allDeps list even if an exception is raised here.
                if( !await ReadMissingDependencies( monitor, info, allDeps, feeds ) )
                {
                    throw new Exception( $"Unable to resolve a dependency for package '{info.Key}'. See logs for details." );
                }
                // Even if the Add is thread safe (it uses an Interlocked set), I prefer here
                // to serialize the call to Add to avoid intermediate allocations.
                lock( _addLock )
                {
                    if( _cache.Add( monitor, allDeps ) == null || (p = _cache.DB.Find( instance )) == null )
                    {
                        throw new Exception( $"Error while updating the package cache." );
                    }
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

        async Task<bool> DoAddAsync( IActivityMonitor monitor, ArtifactInstance instance, List<FullPackageInfo> allDeps, IPackageFeed[] feeds )
        {
            if( _cache.DB.Find( instance ) != null || allDeps.Any( p => p.Key == instance ) ) return true;
            FullPackageInfo? info = await ReadFullInfoAsync( monitor, instance, feeds );
            if( info == null )
            {
                monitor.Error( $"Unable to find package '{instance}' in feeds {feeds.Where( f => f.ArtifactType == instance.Artifact.Type ).Select( f => f.Name ).Concatenate()}." );
                return false;
            }
            return await ReadMissingDependencies( monitor, info, allDeps, feeds );
        }

        async Task<bool> ReadMissingDependencies( IActivityMonitor monitor, FullPackageInfo info, List<FullPackageInfo> allDeps, IPackageFeed[] feeds )
        {
            // From a starting FullPackageInfo (necessarily not null), recusively crawls the dependencies and always
            // append the starting FullPackageInfo to the allDeps list.
            // On error (a dependency failed to be read, either not found - null - or an exception is being thrown), we use
            // the FullPackageInfo.FeedNames by clearing it: this invalidates the starting FullPackageInfo but we
            // nevertheless add it to the allDeps list so that we can skip it when clearing the request cache (failed resolution
            // only must be kept in cache).
            bool success = true;
            try
            {
                foreach( var d in info.Dependencies )
                {
                    if( !await DoAddAsync( monitor, d.Target, allDeps, feeds ) )
                    {
                        monitor.Error( $"Unable to satisfy dependency '{d.Target}' of package '{info.Key}'." );
                    }
                }
            }
            catch
            {
                success = false;
                throw;
            }
            finally
            {
                if( !success ) info.FeedNames.Clear();
                allDeps.Add( info );
            }
            return success;
        }

        Task<FullPackageInfo?> ReadFullInfoAsync( IActivityMonitor monitor, ArtifactInstance instance, IPackageFeed[] feeds )
        {
            // We use a simple dictionary to handle request duplicates.
            // Here we lookup the list for a pending TaskCompletionSource.
            // If we don't find it, we add a new one and launch the work.
            // The cleanup of this list is done at the end of the root EnsureAsync call.
            TaskCompletionSource<FullPackageInfo?>? tcs = null;
            lock( _currentRequests )
            {
                if( _currentRequests.TryGetValue( instance, out tcs ) )
                {
                    return tcs.Task;
                }
                tcs = new TaskCompletionSource<FullPackageInfo?>();
                _currentRequests.Add( instance, tcs );
            }
            _ = DoReadFullInfoAsync( monitor, instance, tcs, feeds );
            return tcs.Task;
        }

        // This never throws. The TCS is resolved.
        async Task DoReadFullInfoAsync( IActivityMonitor monitor, ArtifactInstance instance, TaskCompletionSource<FullPackageInfo?> result, IPackageFeed[] feeds )
        {
            FullPackageInfo p = new FullPackageInfo();
            List<Exception>? feedExceptions = null;
            Exception? invalidSameError = null;
            foreach( var f in feeds )
            {
                if( f.ArtifactType == instance.Artifact.Type )
                {
                    IPackageInfo? info = null;
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
                            if( !p.CheckSame( info, monitor ) )
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
                    // If data is not consistent accross feeds, this is serious!
                    result.SetException( invalidSameError );
                }
                else
                {
                    // If we have feedExceptions, we forget them (they are logged): we have a package
                    // description and that's the most important.
                    result.SetResult( p );
                }
            }
        }
    }

}
