using CK.Core;
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

        /// <summary>
        /// Gets the info of this feed.
        /// </summary>
        public INuGetFeedInfo Info { get; }

        /// <summary>
        /// Must provide the secret key name.
        /// </summary>
        protected abstract string SecretKeyName { get; }

        /// <summary>
        /// Must resolve the push API key.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The API key or null.</returns>
        protected abstract string ResolvePushAPIKey( IActivityMonitor m );

        /// <summary>
        /// Ensures that the secret behind the <see cref="SecretKeyName"/> is available.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
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
        /// <param name="v">The version.</param>
        /// <returns>True if found, false otherwise.</returns>
        public virtual async Task<bool> ExistsAsync( IActivityMonitor m, string packageId, SVersion v )
        {
            var p = new PackageIdentity( packageId, NuGetVersion.Parse( v.ToString() ) );
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

    }
}
