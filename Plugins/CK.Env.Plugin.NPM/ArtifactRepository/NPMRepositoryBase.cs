using CK.Core;
using CSemVer;
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
    abstract class NPMRepositoryBase
    {
        protected readonly NPMClient Client;
        string _secret;
        Registry _registry;

        internal NPMRepositoryBase(
            NPMClient c,
            PackageQualityFilter qualityFilter,
            string name,
            string url )
        {
            Client = c;
            QualityFilter = qualityFilter;
            Url = url;
            UniqueRepositoryName = NPMClient.NPMType.Name + ':' + name;
        }

        /// <summary>
        /// Get the NPM registry url.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// Must provide the secret key name.
        /// </summary>
        public abstract string SecretKeyName { get; }

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
        /// Gets the range of package quality that is accepted by this feed.
        /// </summary>
        public PackageQualityFilter QualityFilter { get; }

        /// <summary>
        /// This repository handles "NPM" artifact type.
        /// </summary>
        /// <param name="artifactType">Type of the artifact.</param>
        /// <returns>True if this repository artifact type is "NPM", false otherwise.</returns>
        public bool HandleArtifactType( in ArtifactType artifactType ) => artifactType == NPMClient.NPMType;

        /// <summary>
        /// Resolves the target NPM registry.
        /// </summary>
        public Registry GetRegistry( IActivityMonitor m, bool throwOnError = true ) => _registry ?? (_registry = CreateRegistry( m, throwOnError ));

        /// <summary>
        /// Creates the registry.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="throwOnError">If true, throw when the <see cref="Registry"/> cannot be instantied.</param>
        /// <returns>The initialized registry.</returns>
        protected abstract Registry CreateRegistry( IActivityMonitor m, bool throwOnError );

        /// <summary>
        /// Overridden to return the <see cref="UniqueRepositoryName"/> string.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => UniqueRepositoryName;

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
        public virtual Task<bool> ExistsAsync( IActivityMonitor m, string packageId, SVersion version )
        {
            return GetRegistry( m, true ).ExistAsync( m, packageId, version );
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
                    if( await GetRegistry( m, true ).ExistAsync( m, file.Instance.Artifact.Name, file.Instance.Version ) )
                    {
                        m.Info( $"Package '{file.Instance}' already in '{ToString()}'. Push skipped." );
                        skipped.Add( file );
                    }
                    else
                    {
                        string firstDistTag = file.Instance.Version.PackageQuality.GetLabels()[0].ToString();
                        using( FileStream fileStream = File.OpenRead( file.FullPath ) )
                        {
                            if( GetRegistry( m, true ).Publish( m,
                                    tarballPath: file.FullPath,
                                    isPublic: arePublicArtifacts,
                                    scope: file.PackageScope,
                                    distTag: firstDistTag ) )
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
                            success &= await GetRegistry( m, true ).AddDistTag( m, file.Instance.Artifact.Name, file.Instance.Version, label.ToString() );
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
                var accepted = locals.Where( l => QualityFilter.Accepts( l.Instance.Version.PackageQuality ) ).ToList();
                if( accepted.Count == 0 )
                {
                    m.Info( $"No packages accepted by '{QualityFilter}' filter for '{UniqueRepositoryName}'." );
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
