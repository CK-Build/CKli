using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin
{
    /// <summary>
    /// Encapsulates the graph of the Repo/Versions as <see cref="RepoReleaseInfo"/> nodes.
    /// </summary>
    public sealed class ReleaseDatabase
    {
        readonly VersionTagPlugin _versionTagPlugin;
        readonly ImmutableArray<VersionTagInfo> _allVersionTagInfos;
        readonly Dictionary<RepoKey, RepoReleaseInfo> _releaseInfo;
        readonly Lock _dbLock;
        Dictionary<PackageInstance, RepoKey>? _producedIndex;

        internal ReleaseDatabase( VersionTagPlugin versionTag, ImmutableArray<VersionTagInfo> versionTagInfos )
        {
            _versionTagPlugin = versionTag;
            _allVersionTagInfos = versionTagInfos;
            _releaseInfo = new Dictionary<RepoKey, RepoReleaseInfo>();
            _dbLock = new Lock();
        }

        internal ArtifactHandlerPlugin ArtifactHandlerPlugin => _versionTagPlugin._artifactHandlerPlugin;

        /// <summary>
        /// Gets the <see cref="RepoReleaseInfo"/> for a <see cref="TagCommit"/> that must not be <see cref="TagCommit.IsFakeVersion"/>
        /// or a <see cref="System.ArgumentException"/> is thrown.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="tagCommit">The existing, non fake, tag commit.</param>
        /// <param name="ci0version">True to consider the <see cref="TagCommit.CI0Version"/> instead of the <see cref="TagCommit.Version"/>.</param>
        /// <returns>The information or null if the released version doesn't exist.</returns>
        public RepoReleaseInfo GetReleaseInfo( IActivityMonitor monitor, TagCommit tagCommit, bool ci0version = false )
        {
            Throw.CheckArgument( !tagCommit.IsFakeVersion );
            var v = ci0version ? tagCommit.CI0Version : tagCommit.Version;
            if( v == null )
            {
                Throw.ArgumentException( nameof( ci0version ), $"{tagCommit} has no ci.0 version." );
            }
            lock( _dbLock )
            {
                return GetReleasedInfo( monitor, new RepoKey( tagCommit, v ) );
            }
        }

        RepoReleaseInfo GetReleasedInfo( IActivityMonitor monitor, RepoKey key )
        {
            Throw.DebugAssert( _dbLock.IsHeldByCurrentThread );
            if( !_releaseInfo.TryGetValue( key, out var r ) )
            {
                BuildContentInfo? content = key.TagCommit.BuildContentInfo;
                Throw.DebugAssert( "TagCommit is not a +fake nor a +deprecated.", content != null );
                var directProducers = new List<RepoReleaseInfo>();
                var allProducers = new HashSet<RepoReleaseInfo>();
                foreach( var consumed in content.Consumed )
                {
                    // If we can't find a producer for a consumed package, it is an external package.
                    if( FindProducer( consumed, out RepoKey producerKey ) )
                    {
                        var p = GetReleasedInfo( monitor, producerKey );
                        directProducers.Add( p );
                        allProducers.UnionWith( p.AllProducers );
                    }
                }
                for( int i = 0; i < directProducers.Count; i++ )
                {
                    var producer = directProducers[i];
                    if( allProducers.Contains( producer ) )
                    {
                        directProducers.RemoveAt( i-- );
                    }
                }
                r = new RepoReleaseInfo( this, key, content, directProducers, allProducers );
                _releaseInfo.Add( key, r );
            }
            return r;
        }

        internal bool FindProducer( PackageInstance p, out RepoKey repo ) => EnsureProducedIndex().TryGetValue( p, out repo );

        Dictionary<PackageInstance, RepoKey> EnsureProducedIndex()
        {
            return _producedIndex ??= CreateIndex( _allVersionTagInfos );

            static Dictionary<PackageInstance, RepoKey> CreateIndex( in ImmutableArray<VersionTagInfo> allVersionTagInfos )
            {
                var index = new Dictionary<PackageInstance, RepoKey>();
                foreach( var vInfo in allVersionTagInfos )
                {
                    var r = vInfo.Repo;
                    foreach( TagCommit tc in vInfo.AllTagCommits )
                    {
                        // Filters out +fake without CI0VersionTag.
                        if( tc.BuildContentInfo == null ) continue;
                        if( !tc.IsFakeVersion )
                        {
                            foreach( var packageId in tc.BuildContentInfo.Produced )
                            {
                                AddPackage( index, tc, tc.Version, packageId );
                            }
                        }
                        if( tc.CI0Version != null )
                        {
                            foreach( var packageId in tc.BuildContentInfo.Produced )
                            {
                                AddPackage( index, tc, tc.CI0Version, packageId );
                            }
                        }
                    }
                }
                return index;

                static void AddPackage( Dictionary<PackageInstance, RepoKey> index, TagCommit c, SVersion v, string packageId )
                {
                    var p = new PackageInstance( packageId, v );
                    var repoKey = new RepoKey( c, v );
                    if( !index.TryAdd( p, repoKey ) )
                    {
                        var exists = index[p];
                        Throw.CKException( $"""
                                            Package '{p}' is claimed to be produced by '{exists}' and '{repoKey}'.
                                            One of them lies.
                                            """ );
                    }
                }
            }
        }

        internal IReadOnlyList<RepoReleaseInfo> GetDirectConsumers( IActivityMonitor monitor, RepoReleaseInfo info )
        {
            lock( _dbLock )
            {
                // Collecting in a HashSet: duplicated RepoKey is removed,
                // this handles the Package -> Solution projection.
                var consumers = new HashSet<RepoKey>();
                foreach( var id in info.Content.Produced )
                {
                    CollectConsumers( new PackageInstance( id, info.Version ), consumers );
                }
                // Among these consumers, there are transitive dependencies:
                // it is all the (Repo,Version) that consume a package produced by another consumer
                // but recursively:
                // - CK-Core -> CK.Core
                // - CK-ActivityMonitor -> CK.ActivityMonitor
                //      <- CK.Core
                // - CK-Monitoring -> CK.Monitoring
                //      <- CK.ActivityMonitor
                // - CK-XXX
                //      <- CK.Monitoring
                //      <- CK.Core
                // Here, the single direct consumer of CK-Core is CK.ActivityMonitor.
                // To evict CK-XXX as a direct consumer, we must first discover CK-Monitoring (that is not itself
                // a direct consumer of CK-Core).
                // Instead of implementing here the mirror of the RepoReleaseInfo, we use them (the "past") to filter
                // the direct consumers (the "future").
                //
                var consumerInfos = new List<RepoReleaseInfo>();
                foreach( var c in consumers )
                {
                    consumerInfos.Add( GetReleasedInfo( monitor, c ) );
                }
                for( int i = 0; i < consumerInfos.Count; i++ )
                {
                    var candidate = consumerInfos[i];
                    for( int j = 0; j < consumerInfos.Count; j++ )
                    {
                        if( i == j ) continue;
                        var other = consumerInfos[j];
                        if( other == candidate || other.AllProducers.Contains( candidate ) )
                        {
                            consumerInfos.RemoveAt( i-- );
                            break;
                        }
                    }
                }
                return consumerInfos;
            }
        }

        void CollectConsumers( in PackageInstance p, HashSet<RepoKey> collector )
        {
            foreach( var vInfo in _allVersionTagInfos )
            {
                var r = vInfo.Repo;
                foreach( TagCommit tc in vInfo.AllTagCommits )
                {
                    if( tc.IsRegularVersion )
                    {
                        if( tc.BuildContentInfo.Consumed.Contains( p ) )
                        {
                            collector.Add( new RepoKey(tc, tc.Version) );
                        }
                    }
                }
            }
        }
    }


    /// <summary>
    /// Initializes the <see cref="ReleaseDatabase"/>.
    /// This calls <see cref="RepoPluginBase{T}.TryGetAllWithoutIssue(IActivityMonitor, out ImmutableArray{T}, string?)"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The version database.</returns>
    public ReleaseDatabase? EnsureDatabase( IActivityMonitor monitor )
    {
        if( _releaseDatabase == null && TryGetAllWithoutIssue( monitor, out var allVersionTagInfo ) )
        {
            _releaseDatabase = new ReleaseDatabase( this, allVersionTagInfo );
        }
        return _releaseDatabase;
    }
}
