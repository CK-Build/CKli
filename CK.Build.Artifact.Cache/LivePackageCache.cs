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
        readonly IPackageFeed[] _feeds;
        readonly List<(ArtifactInstance, TaskCompletionSource<IFullPackageInfo?>)> _currentRequests;
        readonly object _addLock;
        readonly bool _isFullFeeds;

        public LivePackageCache( PackageCache cache, IEnumerable<IPackageFeed> feeds, bool isFullFeeds )
        {
            _cache = cache;
            _feeds = feeds.ToArray();
            _currentRequests = new List<(ArtifactInstance, TaskCompletionSource<IFullPackageInfo?>)>();
            _addLock = new object();
            _isFullFeeds = isFullFeeds;
        }

        /// <summary>
        /// Gets the package cache that is updated by this updater.
        /// </summary>
        public PackageCache Cache => _cache;

        /// <summary>
        /// Gets the set of feeds.
        /// </summary>
        public IReadOnlyList<IPackageFeed> Feeds => _feeds;

        /// <summary>
        /// Attempts to locate or add a <see cref="PackageInstance"/> and all its dependencies recusively (all
        /// its <see cref="PackageInstance.Reference.BaseTargetKey"/>) to the <see cref="Cache"/>.
        /// Any resolution error (a missing dependency or when all feeds access fail for a package) is an exception.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="instance">The instance to resolve.</param>
        /// <returns>Null if the package cannot be found in any of the <see cref="Feeds"/>.</returns>
        public async Task<PackageInstance?> AddAsync( IActivityMonitor monitor, ArtifactInstance instance )
        {
            var p = _cache.DB.Find( instance );
            if( p != null ) return p;

            IFullPackageInfo? info = await ReadFullInfoAsync( monitor, instance );
            if( info == null ) return null;

            List<IFullPackageInfo>? allDeps = new List<IFullPackageInfo>();
            try
            {
                // The info object will be appended to the allDeps list.
                if( !await ReadMissingDependencies( monitor, info, allDeps ) )
                {
                    throw new Exception( $"Unable to resolve a dependency for package '{info.Key}'. See logs for details." );
                }
                // Even if the Add is thread safe (it uses an Interlocked set), I prefer here
                // to serialize the call to avoid intermediate allocations.
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
                // Cleanup the current requests.
                lock( _currentRequests )
                {
                    foreach( var i in allDeps )
                    {
                        int idx = _currentRequests.IndexOf( c => c.Item1 == i.Key );
                        if( idx >= 0 ) _currentRequests.RemoveAt( idx );
                    }
                }
            }
        }

        async Task<bool> DoAddAsync( IActivityMonitor monitor, ArtifactInstance instance, List<IFullPackageInfo> allDeps )
        {
            if( _cache.DB.Find( instance ) != null || allDeps.Any( p => p.Key == instance ) ) return true;
            IFullPackageInfo? info = await ReadFullInfoAsync( monitor, instance );
            if( info == null )
            {
                monitor.Error( $"Unable to find package '{instance}' in feeds {_feeds.Where( f => f.ArtifactType == instance.Artifact.Type ).Select( f => f.Name ).Concatenate()}." );
                return false;
            }
            return await ReadMissingDependencies( monitor, info, allDeps );
        }

        async Task<bool> ReadMissingDependencies( IActivityMonitor monitor, IFullPackageInfo info, List<IFullPackageInfo> allDeps )
        {
            try
            {
                foreach( var d in info.Dependencies )
                {
                    if( !await DoAddAsync( monitor, d.Target, allDeps ) )
                    {
                        monitor.Error( $"Unable to satisfy dependency '{d.Target}' of package '{info.Key}'." );
                        return false;
                    }
                }
            }
            finally
            {
                allDeps.Add( info );
            }
            return true;
        }

        Task<IFullPackageInfo?> ReadFullInfoAsync( IActivityMonitor monitor, ArtifactInstance instance )
        {
            // We use a simple locked list to handle request duplicates.
            // Here we lookup the list for a pending TaskCompletionSource.
            // If we don't find it, we add a new one and launch the work.
            // The cleanup of this list is done at the root Add call.
            TaskCompletionSource<IFullPackageInfo?>? asyncPath = null;
            lock( _currentRequests )
            {
                int idx = _currentRequests.IndexOf( c => c.Item1 == instance );
                if( idx >= 0 ) return _currentRequests[idx].Item2.Task;
                asyncPath = new TaskCompletionSource<IFullPackageInfo?>();
                _currentRequests.Add( (instance, asyncPath) );
            }
            _ = DoReadFullInfoAsync( monitor, instance, asyncPath );
            return asyncPath.Task;
        }

        async Task DoReadFullInfoAsync( IActivityMonitor monitor, ArtifactInstance instance, TaskCompletionSource<IFullPackageInfo?> result )
        {
            FullPackageInfo p = new FullPackageInfo();
            List<Exception>? feedExceptions = null;
            foreach( var f in _feeds )
            {
                if( f.ArtifactType == instance.Artifact.Type )
                {
                    IPackageInfo? info = null;
                    try
                    {
                        info = await f.GetPackageInfoAsync( monitor, instance );
                    }
                    catch( Exception ex )
                    {
                        if( feedExceptions == null ) feedExceptions = new List<Exception>();
                        feedExceptions.Add( ex );
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
                                throw new Exception( $"Package info for {p.Key} from feed '{p.FeedNames[0]}' differ from the one of '{f.TypedName}'. See previous log error for details." );
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
                // We have at least one feed that has the package.
                // If we have no errors and the feeds are "all the feeds", then we can update the "removed from feeds".
                p.AllFeedNamesAreKnown = _isFullFeeds && feedExceptions == null;
                result.SetResult( p );
            }
        }
    }

}
