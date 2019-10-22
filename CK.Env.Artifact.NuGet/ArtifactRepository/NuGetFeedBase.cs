using CK.Core;
using CSemVer;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Env.NuGet
{
    abstract class NuGetFeedBase
    {
        private protected readonly NuGetClient Client;
        SourceRepository _sourceRepository;
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
                return await _feed.SafeCall( m, ( sources, meta, logger ) => GetAvailable( meta, logger, artifactName ) );
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
        }

        bool _secretEnsured = false;
        void EnsureSecrets( IActivityMonitor m)
        {
            if( _secretEnsured ) return;
            _secretEnsured = true;
            string secret;
            var secretOrName = Feed.Credentials?.PasswordOrSecretKeyName;
            if( Feed.Credentials.IsSecretKeyName )
            {
                secret = Client.SecretKeyStore.GetSecretKey( m, secretOrName, false );
                if( secret == null )
                {
                    m.Error( $"Missing secret key. No available secret available for {secretOrName}." );
                    throw new InvalidOperationException();
                }
            }
            else
            {
                secret = secretOrName;
            }
            NuGetClient.EnsureVSSFeedEndPointCredentials( m, PackageSource.Source, secret );
        }

        internal NuGetFeedBase( IActivityMonitor m, NuGetClient c, string url, string name, SimpleCredentials creds )
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

        protected async Task<T> SafeCall<T>( IActivityMonitor m, Func<SourceRepository, MetadataResource, NuGetLoggerAdapter, Task<T>> f )
        {
            EnsureSecrets( m );
            bool retry = false;
            var logger = new NuGetLoggerAdapter( m );
            NuGetClient.Initalize( logger, out var mustRefresh );
            if( mustRefresh || _sourceRepository == null )
            {
                _sourceRepository = new SourceRepository( PackageSource, NuGetClient.StaticProviders );
            }
        again:
            MetadataResource meta = null;
            try
            {
                meta = await _sourceRepository.GetResourceAsync<MetadataResource>();
                return await f( _sourceRepository, meta, logger );
            }
            catch( MissingRequiredSecretException )
            {
                throw; //It's useless to retry in this case
            }
            catch( Exception ex )
            {
                if( meta != null && !retry )
                {
                    retry = true;
                    if( CanRetry( meta, logger, ex ) )
                    {
                        goto again;
                    }
                }
                throw;
            }
        }

        private protected abstract bool CanRetry( MetadataResource meta, NuGetLoggerAdapter logger, Exception ex );
    }
}
