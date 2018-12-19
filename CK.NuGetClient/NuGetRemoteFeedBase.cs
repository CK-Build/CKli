using CK.Core;
using CK.Env;
using CK.Text;
using CSemVer;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.NuGetClient
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// </summary>
    abstract class NuGetRemoteFeedBase : IInternalFeed
    {
        protected readonly NuGetClient Client;
        internal readonly PackageSource _packageSource;
        readonly SourceRepository _sourceRepository;
        readonly AsyncLazy<PackageUpdateResource> _updater;
        readonly AsyncLazy<MetadataResource> _meta;
        string _secret;

        internal NuGetRemoteFeedBase( NuGetClient c, PackageSource source, INuGetFeedInfo info )
        {
            Info = info;
            Client = c;
            _packageSource = source;
            _sourceRepository = new SourceRepository( _packageSource, NuGetClient.Providers );
            _updater = new AsyncLazy<PackageUpdateResource>( () => _sourceRepository.GetResourceAsync<PackageUpdateResource>() );
            _meta = new AsyncLazy<MetadataResource>( () => _sourceRepository.GetResourceAsync<MetadataResource>() );
        }

        void IInternalFeed.CollectPackageSources( List<PackageSource> collector )
        {
            collector.Add( _packageSource );
        }

        IArtifactRepositoryInfo IArtifactRepository.Info => Info;

        /// <summary>
        /// Gets the info of this feed.
        /// </summary>
        public INuGetFeedInfo Info { get; }

        /// <summary>
        /// Must provide the secret key name.
        /// When null or empty, <see cref="ResolveSecret"/> always return null.
        /// </summary>
        public abstract string SecretKeyName { get; }

        /// <summary>
        /// Must resolve the push API key.
        /// The push API key is not necessarily the secret behind <see cref="SecretKeyName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The API key or null.</returns>
        protected abstract string ResolvePushAPIKey( IActivityMonitor m );

        /// <summary>
        /// Ensures that the secret behind the <see cref="SecretKeyName"/> is available.
        /// This always returns null if <see cref="SecretKeyName"/> is null.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="throwOnEmpty">
        /// True to throw if SecretKeyName is not null or empty and the secret can not be resolved.
        /// </param>
        /// <returns>The non empty secret or null.</returns>
        public virtual string ResolveSecret( IActivityMonitor m, bool throwOnEmpty = false )
        {
            if( _secret == null )
            {
                var s = SecretKeyName;
                if( !String.IsNullOrWhiteSpace( s ) )
                {
                    _secret = Client.SecretKeyStore.GetSecretKey( m, s, throwOnEmpty, $"Needed for feed '{Info}'." );
                }
            }
            return String.IsNullOrWhiteSpace( _secret ) ? null : _secret;
        }

        /// <summary>
        /// Cheks whether a versioned package exists in this feed.
        /// </summary>
        /// <param name="m">The activity monitor.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The version.</param>
        /// <returns>True if found, false otherwise.</returns>
        public virtual async Task<bool> ExistsAsync( IActivityMonitor m, string packageId, SVersion version )
        {
            if( packageId == null ) throw new ArgumentNullException( nameof( packageId ) );
            if( version == null ) throw new ArgumentNullException( nameof( version ) );
            var p = new PackageIdentity( packageId, NuGetVersion.Parse( version.ToString() ) );
            var meta = await _meta;
            return await meta.Exists( p, Client.SourceCache, new NuGetLoggerAdapter( m ), CancellationToken.None ); 
        }

        /// <summary>
        /// Pushes a set of packages.
        /// </summary>
        /// <param name="ctx">The monitor to use.</param>
        /// <param name="files">The set of packages to push.</param>
        /// <param name="timeoutSeconds">Timeout in seconds.</param>
        /// <returns>The awaitable.</returns>
        public async Task PushPackagesAsync( IActivityMonitor m, IEnumerable<LocalNuGetPackageFile> files, int timeoutSeconds = 20 )
        {
            string apiKey = ResolvePushAPIKey( m );
            if( string.IsNullOrEmpty( apiKey ) )
            {
                m.Warn( $"Could not resolve API key. Push to '{Info}' is skipped." );
                return;
            }
            try
            {
                var logger = new NuGetLoggerAdapter( m );
                var updater = await _updater;
                foreach( var f in files )
                {
                    await updater.Push(
                        f.FullPath,
                        String.Empty, // no Symbol source.
                        timeoutSeconds,
                        disableBuffering: false,
                        getApiKey: endpoint => apiKey,
                        getSymbolApiKey: symbolsEndpoint => null,
                        noServiceEndpoint: false,
                        log: logger );
                    await OnPackagePushed( logger, f );
                }
                await OnAllPackagesPushed( logger, files );
            }
            catch( Exception ex )
            {
                m.Error( ex );
            }
        }

        protected virtual Task OnPackagePushed( NuGetLoggerAdapter logger, LocalNuGetPackageFile f )
        {
            return Task.CompletedTask;
        }

        protected virtual Task OnAllPackagesPushed( NuGetLoggerAdapter logger, IEnumerable<LocalNuGetPackageFile> files )
        {
            return Task.CompletedTask;
        }

        public async Task<IArtifactLocator> FindAsync( IActivityMonitor m, string type, string name, SVersion version )
        {
            if( type == null ) throw new ArgumentNullException( nameof( type ) );
            if( name == null ) throw new ArgumentNullException( nameof( name ) );
            if( version == null ) throw new ArgumentNullException( nameof( version ) );

            if( type != "NuGet" ) return null;
            if( await ExistsAsync( m, name, version ) )
            {
                return new RemoteNuGetLocator( new ArtifactInstance( type, name, version ), this );
            }
            return null;
        }

        public async Task<bool> PushAsync( IActivityMonitor m, IEnumerable<IArtifactLocator> artifacts )
        {
            bool success = true;
            using( m.OnError( () => success = false ) )
            {
                var unmanaged = artifacts.Where( a => !(a is LocalNuGetPackageFile) ).ToList();
                if( unmanaged.Count > 0 )
                {
                    m.Error( $"Invalid artifact type for: {unmanaged.Select( a => a.Instance.ToString()).Concatenate()}." );
                }
                await PushPackagesAsync( m, artifacts.OfType<LocalNuGetPackageFile>() );
            }
            return success;
        }
    }
}
