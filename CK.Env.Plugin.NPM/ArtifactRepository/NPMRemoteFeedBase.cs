using CK.Core;
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
        /// Overridden to return the <see cref="Info"/> string.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => Info.ToString();

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
        /// <returns>True on success, false on error..</returns>
        async Task<bool> PushPackagesAsync( IActivityMonitor m, IEnumerable<LocalNPMPackageFile> files, bool arePublicArtifacts )
        {
            bool success = true;
            using( var a = m.OpenInfo( "Pushing packages..." ) )
            {
                var skipped = new List<LocalNPMPackageFile>();
                var pushed = new List<LocalNPMPackageFile>();
                foreach( LocalNPMPackageFile file in files )
                {
                    if( await Registry.ExistAsync( m, file.Instance.Artifact.Name, file.Instance.Version ) )
                    {
                        m.Info( $"Package '{file.Instance}' already in '{ToString()}'. Push skipped." );
                        skipped.Add( file );
                    }
                    else
                    {
                        string firstDistTag = file.Instance.Version.PackageQuality.GetLabels()[0].ToString();
                        using( FileStream fileStream = File.OpenRead( file.FullPath ) )
                        {
                            if( await Registry.Publish( m, file.FullPath, firstDistTag ) )
                            {
                                pushed.Add( file );
                            }
                            else
                            {
                                success = false;
                            }
                        }
                    }
                }
                if( success )
                {
                    foreach( var file in pushed.Concat( skipped ) )
                    {
                        foreach( var label in file.Instance.Version.PackageQuality.GetLabels().Skip( 1 ) )
                        {
                            success &= await Registry.AddDistTag( m, file.Instance.Artifact.Name, file.Instance.Version, label.ToString() );
                        }
                    }
                    if( success )
                    {
                        await OnAllPackagesPushed( m, skipped, pushed );
                    }
                }
            }
            return success;
        }

        /// <summary>
        /// Called even if no package has been pushed.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="skipped">The set of packages skipped because they already exist in the feed.</param>
        /// <param name="pushed">The set of packages pushed.</param>
        /// <returns>The awaitable.</returns>
        protected virtual Task OnAllPackagesPushed( IActivityMonitor m, IReadOnlyList<LocalNPMPackageFile> skipped, IReadOnlyList<LocalNPMPackageFile> pushed )
        {
            return Task.CompletedTask;
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
                var accepted = locals.Where( l => Info.QualityFilter.Accepts( l.Instance.Version.PackageQuality ) ).ToList();
                if( accepted.Count == 0 )
                {
                    m.Info( $"No packages accepted by '{Info.QualityFilter}' filter for '{Info}'." );
                }
                else
                {
                    success &= await PushPackagesAsync( m, accepted, artifacts.ArePublicArtifacts );
                }
            }
            return success;
        }
    }
}
