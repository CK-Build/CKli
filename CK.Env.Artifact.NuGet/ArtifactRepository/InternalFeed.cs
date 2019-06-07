using CK.Core;
using CSemVer;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Env.NuGet
{
    class InternalFeed 
    {
        private protected readonly NuGetClient Client;
        private protected readonly PackageSource _packageSource;
        private protected readonly SourceRepository _sourceRepository;
        private protected readonly AsyncLazy<MetadataResource> _meta;
        INuGetFeed _feed;

        class ReadFeed : INuGetFeed
        {
            readonly InternalFeed _feed;

            public ReadFeed( InternalFeed feed, string name, SimpleCredentials creds )
            {
                _feed = feed;
                Name = name;
                Credentials = creds;
                TypedName = $"{NuGetClient.NuGetType.Name}:{Name}";
            }

            public string Name { get; }

            public string Url { get; }

            public SimpleCredentials Credentials { get; }

            public string TypedName { get; }

            public ArtifactType ArtifactType => NuGetClient.NuGetType;

            public async Task<IReadOnlyCollection<ArtifactAvailableInstances>> GetVersionsAsync( IActivityMonitor m, IEnumerable<string> artifactNames )
            {
                var logger = _feed.EnsureInitialization( m );
                var meta = await _feed._meta;
                var result = new List<ArtifactAvailableInstances>();
                var tasks = artifactNames.Select( n => GetAvailable( meta, logger, n ) );
                await Task.WhenAll( tasks );
                return tasks.Select( t => t.Result ).ToArray();
            }

            async Task<ArtifactAvailableInstances> GetAvailable( MetadataResource meta, NuGetLoggerAdapter logger, string name )
            {
                var v = new ArtifactAvailableInstances( new Artifact( NuGetClient.NuGetType, name ) );
                var latest = await meta.GetVersions( name, true, false, _feed.Client.SourceCache, logger, CancellationToken.None );
                foreach( var sVer in latest.Select( nV => SVersion.Parse( nV.ToNormalizedString() ) ) )
                {
                    v = v.WithVersion( sVer );
                }
                return v;
            }
        }

        private protected InternalFeed( NuGetClient c, PackageSource packageSource )
        {
            Client = c;
            _packageSource = packageSource;
            _sourceRepository = new SourceRepository( _packageSource, NuGetClient.Providers );
            _meta = new AsyncLazy<MetadataResource>( () => _sourceRepository.GetResourceAsync<MetadataResource>() );
        }

        internal InternalFeed( NuGetClient c, string url, string name, SimpleCredentials creds )
            : this( c, new PackageSource( url, name ) )
        {
            HandleFeed( url, name, creds );
        }

        internal INuGetFeed Feed => _feed;

        internal bool MatchFeedFor( string url ) => StringComparer.OrdinalIgnoreCase.Equals( _packageSource.Source, url );

        internal INuGetFeed HandleFeed( string url, string name, SimpleCredentials creds )
        {
            Debug.Assert( _feed == null && MatchFeedFor( url ) );
            return _feed = new ReadFeed( this, name, creds );
        }

        protected virtual NuGetLoggerAdapter EnsureInitialization( IActivityMonitor m )
        {
            var logger = new NuGetLoggerAdapter( m );
            NuGetClient.Initalize( logger );
            return logger;
        }

        internal void CollectPackageSources( List<PackageSource> collector )
        {
            collector.Add( _packageSource );
        }
    }
}
