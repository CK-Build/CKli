using CK.Core;
using CSemVer;
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

        /// <summary>
        /// Implements a feed (a package source) on the basis of an already existing one (typically an artefact target).
        /// The credentials can be null or may differ from the base feed ones.
        /// </summary>
        class ReadFeed : INuGetFeed
        {
            readonly NuGetFeedBase _baseFeed;
            bool? _checkedSecret;

            public ReadFeed( NuGetFeedBase f, string name, SimpleCredentials creds )
            {
                Debug.Assert( f != null );
                _baseFeed = f;
                Name = name;
                Credentials = creds;
                TypedName = $"{NuGetClient.NuGetType.Name}:{Name}";
            }

            public string Name { get; }

            public string Url => _baseFeed.Url;

            public SimpleCredentials Credentials { get; }

            public string TypedName { get; }

            public ArtifactType ArtifactType => NuGetClient.NuGetType;

            public bool CheckSecret( IActivityMonitor m, bool throwOnMissing )
            {
                if( _checkedSecret.HasValue ) return _checkedSecret.Value;
                // If we are bound to a repository that has a secret, its configuration, if available, is the one to use!
                INuGetRepository repo = _baseFeed as INuGetRepository;
                bool isBoundToProtectedRepository = repo != null && !String.IsNullOrEmpty( repo.SecretKeyName );
                if( isBoundToProtectedRepository )
                {
                    var fromRepo = !String.IsNullOrEmpty( repo.ResolveSecret( m, false ) );
                    if( fromRepo )
                    {
                        m.Trace( $"Feed '{Name}' uses secret from repository '{repo.Name}'." );
                        _checkedSecret = true;
                        return true;
                    }
                }
                if( Credentials != null )
                {
                    string secret;
                    if( Credentials.IsSecretKeyName == true )
                    {
                        secret = _baseFeed.Client.SecretKeyStore.GetSecretKey( m, Credentials.PasswordOrSecretKeyName, throwOnMissing );
                        _checkedSecret = secret != null;
                        if( _checkedSecret == true )
                        {
                            m.Trace( $"Feed '{Name}' uses its configured credential '{Credentials.PasswordOrSecretKeyName}'." );
                        }
                    }
                    else
                    {
                        secret = Credentials.PasswordOrSecretKeyName;
                        _checkedSecret = true;
                        m.Trace( $"Feed '{Name}' uses its configured password." );
                    }
                    if( _checkedSecret == true )
                    {
                        NuGetClient.EnsureVSSFeedEndPointCredentials( m, Url, secret );
                    }
                    else
                    {
                        m.Error( $"Feed '{Name}': unable to resolve the credentials." );
                    }
                }
                else
                {
                    // There is no credential: let it be and hope it works.
                    m.Trace( $"Feed '{Name}' has no available secret. It must be a public feed." );
                }
                return _checkedSecret ?? false;
            }

            public async Task<ArtifactAvailableInstances> GetVersionsAsync( IActivityMonitor m, string artifactName )
            {
                return await _baseFeed.SafeCall( m, ( sources, meta, logger ) => GetAvailable( meta, logger, artifactName ) );
            }

            async Task<ArtifactAvailableInstances> GetAvailable( MetadataResource meta, NuGetLoggerAdapter logger, string name )
            {
                var v = new ArtifactAvailableInstances( this, name );
                var latest = await meta.GetVersions( name, true, false, _baseFeed.Client.SourceCache, logger, CancellationToken.None );
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

        /// <summary>
        /// Constructor for internal <see cref="NuGetClient.PureFeed"/>: a pure feed carries only
        /// a <see cref="Feed"/>, it is not a <see cref="NuGetRepositoryBase"/>.
        /// </summary>
        internal NuGetFeedBase( IActivityMonitor m, NuGetClient c, string url, string name, SimpleCredentials creds )
            : this( c, new PackageSource( url, name ) )
        {
            HandleFeed( c.SecretKeyStore, url, name, creds );
        }

        private protected NuGetFeedBase( NuGetClient c, PackageSource packageSource )
        {
            Client = c;
            PackageSource = packageSource;
        }

        internal readonly PackageSource PackageSource;

        /// <summary>
        /// Associated source feed. Null when this is a Repository and no feed definition is associated to this repository.
        /// </summary>
        internal INuGetFeed Feed
        {
            get
            {
                Debug.Assert( _feed != null || this is INuGetRepository );
                return _feed;
            }
        }

        public string Url => PackageSource.Source;

        public string Name => PackageSource.Name;

        internal INuGetFeed HandleFeed( SecretKeyStore keyStore, string url, string name, SimpleCredentials creds )
        {
            Debug.Assert( _feed == null && url.Equals( Url, StringComparison.OrdinalIgnoreCase ) );
            if( creds?.IsSecretKeyName == true )
            {
                keyStore.DeclareSecretKey( creds.PasswordOrSecretKeyName, current => current?.Description
                                    ?? $"Used to enable CKli to retrieve informations about NuGet packages from feed '{name}' and to configure NuGet.config file." );
            }
            return _feed = new ReadFeed( this, name, creds );
        }

        protected async Task<T> SafeCall<T>( IActivityMonitor m, Func<SourceRepository, MetadataResource, NuGetLoggerAdapter, Task<T>> f )
        {
            bool retry = false;
            var logger = new NuGetLoggerAdapter( m );
            if( _sourceRepository == null )
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
