using CK.Core;
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
using System.Threading;
using System.Threading.Tasks;

namespace CK.Env.NuGet
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// </summary>
    abstract class NuGetRepositoryBase : NuGetFeedBase, IArtifactRepository
    {
        readonly AsyncLazy<PackageUpdateResource> _updater;
        string _secret;

        internal NuGetRepositoryBase(
            NuGetClient c,
            PackageSource source,
            PackageQualityFilter qualityFilter )
            : base( c, source )
        {
            QualityFilter = qualityFilter;
            _updater = new AsyncLazy<PackageUpdateResource>( () => _sourceRepository.GetResourceAsync<PackageUpdateResource>() );
            UniqueRepositoryName = NuGetClient.NuGetType.Name + ':' + source.Name;
        }

        /// <summary>
        /// Gets the unique name of this repository.
        /// It should uniquely identify the repository in any context and may contain type, address, urls, or any information
        /// that helps defining unicity.
        /// <para>
        /// This name depends on the repository type. When described externally in xml, the "CheckName" attribute when it exists
        /// must be exactly this computed name.
        /// </para>
        /// </summary>
        public string UniqueRepositoryName { get; }

        /// <summary>
        /// Gets whether the secret is available.
        /// </summary>
        public bool IsAvailable => String.IsNullOrEmpty( SecretKeyName )
                                    || Client.SecretKeyStore.IsSecretKeyAvailable( SecretKeyName ) == true;

        /// <summary>
        /// This repository handles "NuGet" artifact type.
        /// </summary>
        /// <param name="artifactType">Type of the artifact.</param>
        /// <returns>True if this repository artifact type is "NuGet", false otherwise.</returns>
        public bool HandleArtifactType( in ArtifactType artifactType ) => artifactType == NuGetClient.NuGetType;

        /// <summary>
        /// Must resolve the push API key.
        /// The push API key is not necessarily the secret behind <see cref="SecretKeyName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The API key or null.</returns>
        protected abstract string ResolvePushAPIKey( IActivityMonitor m );

        /// <summary>
        /// Gets the range of package quality that is accepted by this feed.
        /// </summary>
        public PackageQualityFilter QualityFilter { get; }
        
        /// <summary>
        /// Must provide the secret key name.
        /// A null or empty SecretKeyName means that the repository does not require any protection.
        /// </summary>
        public abstract string SecretKeyName { get; }

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
                    _secret = Client.SecretKeyStore.GetSecretKey( m, s, throwOnEmpty );
                    if( _secret != null ) OnSecretResolved( m, _secret ); 
                }
            }
            return String.IsNullOrWhiteSpace( _secret ) ? null : _secret;
        }

        protected virtual void OnSecretResolved( IActivityMonitor m, string secret )
        {
        }

        private protected override bool CanRetry( MetadataResource meta, NuGetLoggerAdapter logger, Exception ex )
        {
            var secretName = SecretKeyName;
            if( !String.IsNullOrWhiteSpace( secretName ) )
            {
                var updated = Client.SecretKeyStore.GetSecretKey( logger.Monitor, secretName, false );
                if( updated != null && updated != _secret )
                {
                    OnSecretResolved( logger.Monitor, _secret = updated );
                    logger.Monitor.Warn( "NuGet request failed but an updated secret is available. Retrying.", ex );
                    return true;
                }
            }
            logger.Monitor.Trace( "NuGet request failed. No updated secret available. Rethrowing the exception." );
            return false;
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
            return await SafeCall( m, ( meta, logger ) => meta.Exists( p, Client.SourceCache, logger, CancellationToken.None ) );
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
            try
            {
                await SafeCall( m, async ( meta, logger ) =>
                {
                    var exist = files.Select( file => (file, id: new PackageIdentity( file.PackageId, NuGetVersion.Parse( file.Version.ToNuGetPackageString() ) )) )
                                     .Select( fId => (fId.file, exists: meta.Exists( fId.id, Client.SourceCache, logger, CancellationToken.None )) )
                                     .ToArray();
                    await Task.WhenAll( exist.Select( e => e.exists ) );

                    var toSkip = exist.Where( e => e.exists.Result ).Select( e => e.file ).ToArray();
                    if( toSkip.Length > 0 )
                    {
                        var existing = toSkip.Select( f => f.ToString() ).Concatenate();
                        m.Info( $"Already existing packages, push skipped for: " + existing );
                    }
                    var toPush = exist.Where( e => !e.exists.Result ).Select( e => e.file ).ToArray();
                    var updater = await _updater;
                    foreach( var f in toPush )
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
                    await OnAllPackagesPushed( logger, toSkip, toPush );
                    return 0;
                } );
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

        /// <summary>
        /// Called even if no package has been pushed.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="skipped">The set of packages skipped because they already exist in the feed.</param>
        /// <param name="pushed">The set of packages pushed.</param>
        /// <returns>The awaitable.</returns>
        protected virtual Task OnAllPackagesPushed( NuGetLoggerAdapter logger, IReadOnlyList<LocalNuGetPackageFile> skipped, IReadOnlyList<LocalNuGetPackageFile> pushed )
        {
            return Task.CompletedTask;
        }

        public async Task<bool> PushAsync( IActivityMonitor m, IArtifactLocalSet artifacts )
        {
            bool success = true;
            using( m.OnError( () => success = false ) )
            {
                if( !(artifacts is IEnumerable<LocalNuGetPackageFile> locals) )
                {
                    m.Error( $"Invalid artifact local set for NuGet feed." );
                    return false;
                }
                var accepted = locals.Where( l => QualityFilter.Accepts( l.Version.PackageQuality ) ).ToList();
                if( accepted.Count == 0 )
                {
                    m.Info( $"No packages accepted by {QualityFilter} filter for {UniqueRepositoryName}." );
                }
                else
                {
                    await PushPackagesAsync( m, accepted );
                }
            }
            return success;
        }

        /// <summary>
        /// Overridden to return the <see cref="UniqueRepositoryName"/> string.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => UniqueRepositoryName;

    }
}
