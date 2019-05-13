using CK.Core;
using CK.Env;
using CSemVer;
using Npm.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// </summary>
    abstract class NPMRemoteFeedBase : INPMFeed
    {
        protected readonly NPMClient Client;
        string _secret;

        internal NPMRemoteFeedBase( NPMClient c, INPMFeedInfo info, Registry registry )
        {
            Info = info;
            Registry = registry;
            Client = c;
        }

        /// <summary>
        /// This repository handles "NPM" artifact type.
        /// </summary>
        /// <param name="artifactType">Type of the artifact.</param>
        /// <returns>True if this repository artifact type is "NPM", false otherwise.</returns>
        public bool HandleArtifactType( in ArtifactType artifactType ) => artifactType == NPMClient.NPMType;

        IArtifactRepositoryInfo IArtifactRepository.Info => Info;

        /// <summary>
        /// Gets the info of this feed.
        /// </summary>
        public INPMFeedInfo Info { get; }

        public Registry Registry { get; }

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
                var s = Info.SecretKeyName;
                if( !String.IsNullOrWhiteSpace( s ) )
                {
                    _secret = Client.SecretKeyStore.GetSecretKey( m, s, throwOnEmpty, $"Needed for feed '{Info}'." );
                    if( _secret != null ) OnSecretResolved( m, _secret );
                }
            }
            return String.IsNullOrWhiteSpace( _secret ) ? null : _secret;
        }

        protected virtual void OnSecretResolved( IActivityMonitor m, string secret )
        {
        }

        /// <summary>
        /// Cheks whether a versioned package exists in this feed.
        /// </summary>
        /// <param name="m">The activity monitor.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The version.</param>
        /// <returns>True if found, false otherwise.</returns>
        public virtual Task<bool> ExistsAsync( IActivityMonitor m, string packageId, SVersion version )
        {
            return Registry.ExistAsync( m, packageId, version );
        }


        /// <summary>
        /// Pushes a set of packages.
        /// </summary>
        /// <param name="ctx">The monitor to use.</param>
        /// <param name="files">The set of packages to push.</param>
        /// <param name="timeoutSeconds">Timeout in seconds.</param>
        /// <returns>The awaitable.</returns>
        public async Task PushPackagesAsync( IActivityMonitor m, IEnumerable<LocalNPMPackageFile> files, int timeoutSeconds = 20 )
        {
            using( var a = m.OpenInfo( "Pushing packages..." ) )
            {
                foreach( LocalNPMPackageFile file in files )
                {
                    using( Stream fileStream = File.OpenRead( file.FullPath ) )
                    {
                        var tags = file.Version.PackageQuality.GetLabels().Select( p => p.ToString() ).ToList();
                        await Registry.Publish( m, fileStream, tags.First() );
                        foreach( string tag in tags.Skip( 1 ) )
                        {
                            await Registry.AddDistTag( m, file.PackageId, file.Version, tag );
                        }
                    }
                }
            }
        }

        public async Task<bool> PushAsync( IActivityMonitor m, IArtifactLocalSet artifacts )
        {
            bool success = true;
            using( m.OnError( () => success = false ) )
            {
                if( !(artifacts is IEnumerable<LocalNPMPackageFile> locals) )
                {
                    m.Error( $"Invalid artifact local set for NPM feed." );
                    return false;
                }
                await PushPackagesAsync( m, locals );
            }
            return success;
        }
    }
}
