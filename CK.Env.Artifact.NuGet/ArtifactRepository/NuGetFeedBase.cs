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
    class NuGetFeedBase 
    {
        private protected readonly NuGetClient Client;
        private protected readonly SourceRepository _sourceRepository;
        private protected readonly AsyncLazy<MetadataResource> _meta;
        INuGetFeed _feed;

        class ReadFeed : INuGetFeed
        {
            readonly NuGetFeedBase _feed;

            public ReadFeed( NuGetFeedBase feed, string name, SimpleCredentials creds )
            {
                _feed = feed;
                Name = name;
                Credentials = creds;
                TypedName = $"{NuGetClient.NuGetType.Name}:{Name}";
            }

            public string Name { get; }

            public string Url => _feed.Url;

            public SimpleCredentials Credentials { get; }

            public string TypedName { get; }

            public ArtifactType ArtifactType => NuGetClient.NuGetType;

            public async Task<ArtifactAvailableInstances> GetVersionsAsync( IActivityMonitor m, string artifactName )
            {
                var logger = _feed.EnsureInitialization( m );
                var meta = await _feed._meta;
                return await GetAvailable( meta, logger, artifactName );
            }

            async Task<ArtifactAvailableInstances> GetAvailable( MetadataResource meta, NuGetLoggerAdapter logger, string name )
            {
                var v = new ArtifactAvailableInstances( this, name );
                var latest = await meta.GetVersions( name, true, false, _feed.Client.SourceCache, logger, CancellationToken.None );
                foreach( var nugetVersion in latest )
                {
                    var vText = nugetVersion.ToNormalizedString();
                    var sV = SVersion.TryParse( vText );
                    if( !sV.IsValid )
                    {
                        logger.Monitor.Warn( $"Unable to parse version '{vText}' for '{name}': {sV.ErrorMessage}" );
                    }
                    else v = v.WithVersion( sV );
                }
                return v;
            }
        }

        private protected NuGetFeedBase( NuGetClient c, PackageSource packageSource )
        {
            Client = c;
            PackageSource = packageSource;
            _sourceRepository = new SourceRepository( PackageSource, NuGetClient.StaticProviders );
            _meta = new AsyncLazy<MetadataResource>( () => _sourceRepository.GetResourceAsync<MetadataResource>() );
        }

        internal NuGetFeedBase( NuGetClient c, string url, string name, SimpleCredentials creds )
            : this( c, new PackageSource( url, name ) )
        {
            HandleFeed( url, name, creds );
        }

        internal readonly PackageSource PackageSource;

        internal INuGetFeed Feed => _feed;

        public string Url => PackageSource.Source;

        public string Name => PackageSource.Name;

        internal INuGetFeed HandleFeed( string url, string name, SimpleCredentials creds )
        {
            Debug.Assert( _feed == null && url.Equals( Url, StringComparison.OrdinalIgnoreCase ) );
            return _feed = new ReadFeed( this, name, creds );
        }

        protected virtual NuGetLoggerAdapter EnsureInitialization( IActivityMonitor m )
        {
            var logger = new NuGetLoggerAdapter( m );
            NuGetClient.Initalize( logger );
            return logger;
        }

    }
}
