using CK.Core;
using CK.Build;
using CK.SimpleKeyVault;
using CSemVer;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NuGet.Frameworks;
using System.Linq;
using CK.Build.PackageDB;
using CK.PerfectEvent;
using NuGet.Packaging;
using System.IO;

namespace CK.Env.NuGet
{
    abstract class NuGetFeedBase
    {
        private protected readonly NuGetClient Client;

        // Instantiated at first need by SafeCall helper.
        object _lock;
        SourceRepository? _sourceRepository;

        // Null when this is a INuGetRepository and no feed definition is associated to this repository.
        INuGetFeed? _feed;

        /// <summary>
        /// Implements a feed (a package source) on the basis of an already existing one (typically a INuGetRepository).
        /// The credentials can be null or may differ from the base feed ones.
        /// </summary>
        sealed class ReadFeed : INuGetFeed, IPackageFeed
        {
            readonly NuGetFeedBase _baseFeed;
            readonly PerfectEventSender<IPackageFeed, RawPackageInfoEventArgs> _feedPackageInfoObtained;

            public ReadFeed( NuGetFeedBase f, string name, SimpleCredentials? creds )
            {
                Debug.Assert( f != null );
                _baseFeed = f;
                _feedPackageInfoObtained = new PerfectEventSender<IPackageFeed, RawPackageInfoEventArgs>();
                Name = name;
                Credentials = creds;
                TypedName = $"{NuGetClient.NuGetType.Name}:{Name}";
            }

            public string Name { get; }

            public string Url => _baseFeed.Url;

            public SimpleCredentials? Credentials { get; }

            public string TypedName { get; }

            public ArtifactType ArtifactType => NuGetClient.NuGetType;

            public PerfectEvent<IPackageFeed, RawPackageInfoEventArgs> FeedPackageInfoObtained => _feedPackageInfoObtained.PerfectEvent;

            public bool UseFullInformation { get; private set; }

            public void SetUseFullInformation() => UseFullInformation = true;

            public void ConfigureCredentials( IActivityMonitor m )
            {
                string? secret = null;
                // If we are bound to a repository that has a secret, its configuration, if available, is the one to use!
                INuGetRepository? repo = _baseFeed as INuGetRepository;
                bool isBoundToProtectedRepository = repo != null && !String.IsNullOrEmpty( repo.SecretKeyName );
                if( isBoundToProtectedRepository )
                {
                    Debug.Assert( repo != null );
                    var fromRepo = !String.IsNullOrEmpty( repo.ResolveSecret( m, throwOnEmpty: false ) );
                    if( fromRepo )
                    {
                        m.Trace( $"Feed '{Name}' uses secret from repository '{repo.Name}'." );
                        if( Credentials != null )
                        {
                            m.Warn( $"Feed '{Name}' won't use the provided Credentials since it uses the secret from repository '{repo.Name}'." );
                        }
                        return;
                    }
                }
                if( Credentials != null )
                {
                    if( Credentials.IsSecretKeyName )
                    {
                        secret = _baseFeed.Client.SecretKeyStore.GetSecretKey( m, Credentials.PasswordOrSecretKeyName, true );
                        m.Trace( $"Feed '{Name}' uses its configured credential '{Credentials.PasswordOrSecretKeyName}'." );
                    }
                    else
                    {
                        secret = Credentials.PasswordOrSecretKeyName;
                        if( secret != null )
                        {
                            m.Trace( $"Feed '{Name}' uses its configured password." );
                        }
                        else
                        {
                            m.Warn( $"Feed '{Name}' provided Credentials with UserName '{Credentials.UserName}' has no password or secret key name." );
                        }
                    }
                }
                if( secret != null )
                {
                    NuGetClient.EnsureVSSFeedEndPointCredentials( m, Url, secret );
                }
                else
                {
                    // There is no credential: let it be and hope it works.
                    m.Trace( $"Feed '{Name}' has no available secret. It must be a public feed." );
                }
            }

            public async Task<ArtifactAvailableInstances?> GetVersionsAsync( IActivityMonitor m, string artifactName )
            {
                try
                {
                    return await _baseFeed.SafeCallAsync<MetadataResource, ArtifactAvailableInstances?>( m, ( sources, meta, logger ) => GetAvailableAsync( meta, logger, artifactName ) );
                }
                catch( Exception ex )
                {
                    m.Error( $"Unable to retrieve available versions for '{artifactName}'.", ex );
                    return null;
                }
            }

            async Task<ArtifactAvailableInstances?> GetAvailableAsync( MetadataResource meta, NuGetLoggerAdapter logger, string name )
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

            public async Task<IPackageInstanceInfo?> GetPackageInfoAsync( IActivityMonitor m, ArtifactInstance instance )
            {
                if( UseFullInformation )
                {
                    var packageInfoAndMetadata = await _baseFeed.SafeCallAsync<PackageMetadataResource, (IPackageInstanceInfo? packageInstanceInfo, IPackageSearchMetadata metadata)>( m, ( sources, meta, logger ) => GetPackageInfoAsync( meta, logger, instance, default ) );

                    if( packageInfoAndMetadata.packageInstanceInfo != null )
                    {
                        var gitUrl = await _baseFeed.SafeCallAsync<FindPackageByIdResource, string>( m, ( sources, meta, logger ) => GetPackageRepositoryUrlAsync( meta, logger, instance, default ) );
                        var nugetMetadata = new NugetPackageMetadata { PackageSearchMetadata = (PackageSearchMetadata)packageInfoAndMetadata.metadata, GitUrl = gitUrl };
                        await _feedPackageInfoObtained.SafeRaiseAsync( m, this, new RawPackageInfoEventArgs( packageInfoAndMetadata.packageInstanceInfo, nugetMetadata ) );
                    }

                    return packageInfoAndMetadata.packageInstanceInfo;

                }
                else
                {
                    return await _baseFeed.SafeCallAsync<DependencyInfoResource, IPackageInstanceInfo?>( m, ( sources, meta, logger ) => GetPackageInfoAsync( meta, logger, instance, default ) );
                }
            }



            sealed class PackageInfoReaderFromRemoteSourceDependencyInfo
            {
                static readonly Type _tResolver;
                static readonly MethodInfo _mGetDependencies;
                static readonly FieldInfo _fClient;
                static readonly FieldInfo _fRegResource;

                static PackageInfoReaderFromRemoteSourceDependencyInfo()
                {
                    /*
                    None of the exposed DependencyInfoResource.ResolvePackages returns the whole set of dependencies for a single version.
                    We use the static ResolverMetadataClient internal class GetDependencies method:

                    public static async Task<IEnumerable<RemoteSourceDependencyInfo>> GetDependencies(
                            HttpSource httpClient,
                            Uri registrationUri,
                            string packageId,
                            VersionRange range,
                            SourceCacheContext cacheContext,
                            ILogger log,
                            CancellationToken token)
                    */
                    _tResolver = Type.GetType( "NuGet.Protocol.ResolverMetadataClient, NuGet.Protocol", throwOnError: false )!;
                    _mGetDependencies = _tResolver.GetMethod( "GetDependencies", BindingFlags.Static | BindingFlags.Public )!;
                    var tDependencyInfoResourceV3 = typeof( DependencyInfoResourceV3 );
                    _fClient = tDependencyInfoResourceV3.GetField( "_client", BindingFlags.Instance | BindingFlags.NonPublic )!;
                    _fRegResource = tDependencyInfoResourceV3.GetField( "_regResource", BindingFlags.Instance | BindingFlags.NonPublic )!;
                }

                static void CheckReflection()
                {
                    if( _tResolver == null ) throw new CKException( "NuGet implementation changed: unable to get private class NuGet.Protocol.ResolverMetadataClient." );
                    if( _mGetDependencies == null ) throw new CKException( "NuGet implementation changed: unable to get static NuGet.Protocol.ResolverMetadataClient.GetDependencies method." );
                    if( _fClient == null ) throw new CKException( "NuGet implementation changed: unable to get private field NuGet.Protocol.DependencyInfoResourceV3._fClient." );
                    if( _fRegResource == null ) throw new CKException( "NuGet implementation changed: unable to get private field NuGet.Protocol.DependencyInfoResourceV3._fRegResource." );
                }

                readonly SourceCacheContext _sourceCache;
                readonly RegistrationResourceV3 _regResource;
                readonly HttpSource _client;

                public DependencyInfoResource CurrentInfoResource { get; }

                public PackageInfoReaderFromRemoteSourceDependencyInfo( DependencyInfoResource meta, SourceCacheContext sourceCache )
                {
                    // Check reflection here: avoid type initialization error.
                    CheckReflection();
                    CurrentInfoResource = meta;
                    _sourceCache = sourceCache;
                    _regResource = (RegistrationResourceV3)_fRegResource.GetValue( meta )!;
                    _client = (HttpSource)_fClient.GetValue( meta )!;
                }

                public async Task<(IPackageInstanceInfo?, object?)> GetPackageInfoAsync( NuGetLoggerAdapter logger,
                                                                                        ArtifactInstance instance,
                                                                                        CancellationToken token )
                {
                    var packageId = instance.Artifact.Name;
                    var version = NuGetVersion.Parse( instance.Version.ParsedText );
                    var singleVersion = new VersionRange( minVersion: version, includeMinVersion: true, maxVersion: version, includeMaxVersion: true );
                    // Construct the registration index url
                    var uri = _regResource.GetUri( instance.Artifact.Name );
                    var param = new object[] { _client, uri, packageId, singleVersion, _sourceCache, logger, token };
                    var o = await ((Task<IEnumerable<RemoteSourceDependencyInfo>>)_mGetDependencies.Invoke( null, param )!);
                    if( o == null || !o.Any() ) return (null, null);
                    // Okay... This should never happens... but who really knows ?!
                    var deps = o.SingleOrDefault();
                    if( deps == null )
                    {
                        logger.SafeLog( m =>
                        {
                            using( m.OpenWarn( $"Obtained {o.Count()} dependency groups for single version {instance.Version.ParsedText} of {packageId}. Ignoring them." ) )
                            {
                                foreach( var g in o )
                                {
                                    m.Error( $"{g.ContentUri}: {g.DependencyGroups}." );
                                }
                            }
                        } );
                        return (null, null);
                    }

                    PackageInstanceInfo result = CreateNugetPackageInstanceInfo( logger, instance, deps.Listed, deps.DependencyGroups );
                    return (result, deps);
                }
            }

            private static PackageInstanceInfo CreateNugetPackageInstanceInfo( NuGetLoggerAdapter logger, ArtifactInstance instance, bool isListed, IEnumerable<PackageDependencyGroup> dependencyGroups )
            {
                var result = new PackageInstanceInfo();
                result.State = isListed ? PackageState.None : PackageState.Unlisted;
                result.Key = instance;
                var savors = NuGetClient.Savors.EmptyTrait;
                foreach( var d in dependencyGroups )
                {
                    var tf = NuGetClient.Savors.FindOrCreate( d.TargetFramework.GetShortFolderName() );
                    savors = savors.Union( tf );
                    foreach( var p in d.Packages )
                    {
                        var range = p.VersionRange.ToString();
                        SVersionBound.ParseResult v = SVersionBound.NugetTryParse( range );
                        if( !v.IsValid )
                        {
                            logger.SafeLog( m => m.Warn( $"Unable to parse version '{range}' (Error: {v.Error}) from dependency group '{tf}' of {instance}. Skipped dependency." ) );
                            continue;
                        }
                        if( v.IsApproximated )
                        {
                            logger.SafeLog( m => m.Warn( $"Version '{range}' from dependency group '{tf}' of {instance} has been approximated to '{v.Result}'." ) );
                            continue;
                        }
                        var a = new ArtifactInstance( NuGetClient.NuGetType, p.Id, v.Result.Base );
                        var kind = p.Exclude.Contains( "All" ) ? ArtifactDependencyKind.Development : ArtifactDependencyKind.Transitive;

                        var idxExists = result.Dependencies.IndexOf( d => d.Target == a && d.Lock == v.Result.Lock && d.MinQuality == v.Result.MinQuality && d.Kind == kind );
                        if( idxExists < 0 )
                        {
                            result.Dependencies.Add( (a, v.Result.Lock, v.Result.MinQuality, kind, tf) );
                        }
                        else
                        {
                            var ef = result.Dependencies[idxExists].Savors;
                            Debug.Assert( ef != null );
                            result.Dependencies[idxExists] = (a, v.Result.Lock, v.Result.MinQuality, kind, tf.Union( ef ));
                        }
                    }
                }
                if( !savors.IsEmpty ) result.Savors = savors;
                return result;
            }

            PackageInfoReaderFromRemoteSourceDependencyInfo? _packageReaderRemoteSourceDependencyInfo;

            async Task<IPackageInstanceInfo?> GetPackageInfoAsync( DependencyInfoResource meta, NuGetLoggerAdapter logger, ArtifactInstance instance, CancellationToken token )
            {
                IPackageInstanceInfo? info;
                object? rawInfo;

                if( _packageReaderRemoteSourceDependencyInfo == null || _packageReaderRemoteSourceDependencyInfo.CurrentInfoResource != meta )
                {
                    _packageReaderRemoteSourceDependencyInfo = new PackageInfoReaderFromRemoteSourceDependencyInfo( meta, _baseFeed.Client.SourceCache );
                }
                (info, rawInfo) = await _packageReaderRemoteSourceDependencyInfo.GetPackageInfoAsync( logger, instance, token );

                if( info != null )
                {
                    Debug.Assert( rawInfo != null );
                    await _feedPackageInfoObtained.SafeRaiseAsync( logger.Monitor, this, new RawPackageInfoEventArgs( info, rawInfo ) );
                }
                return info;
            }

            async Task<(IPackageInstanceInfo?, IPackageSearchMetadata)> GetPackageInfoAsync( PackageMetadataResource meta, NuGetLoggerAdapter logger, ArtifactInstance instance, CancellationToken token )
            {
                var packageId = instance.Artifact.Name;
                var version = NuGetVersion.Parse( instance.Version.ParsedText );

                var metadata = await meta.GetMetadataAsync( new PackageIdentity( packageId, version ), _baseFeed.Client.SourceCache, logger, token );

                if( metadata == null ) return (null, null);
                var result = CreateNugetPackageInstanceInfo( logger, instance, metadata.IsListed, metadata.DependencySets );

                return (result, metadata);
            }

            async Task<string> GetPackageRepositoryUrlAsync( FindPackageByIdResource meta, NuGetLoggerAdapter logger, ArtifactInstance instance, CancellationToken token )
            {
                var packageId = instance.Artifact.Name;
                var version = NuGetVersion.Parse( instance.Version.ParsedText );
                var url = string.Empty;

                using( MemoryStream packageStream = new MemoryStream() )
                {
                    await meta.CopyNupkgToStreamAsync(
                        packageId,
                        version,
                        packageStream,
                        _baseFeed.Client.SourceCache,
                        logger,
                        token );

                    packageStream.Position = 0;

                    using( var packageReader = new PackageArchiveReader( packageStream ) )
                    {
                        var nuspecReader = await packageReader.GetNuspecReaderAsync( token );
                        url = nuspecReader.GetRepositoryMetadata().Url;
                    }
                }


                return url;

            }

            public override string ToString() => TypedName;
        }

        /// <summary>
        /// Constructor for internal <see cref="NuGetClient.PureFeed"/>: a pure feed carries only
        /// a <see cref="Feed"/>, it is not a <see cref="NuGetRepositoryBase"/>.
        /// </summary>
        internal NuGetFeedBase( IActivityMonitor monitor, NuGetClient c, string url, string name, SimpleCredentials? creds )
            : this( c, new PackageSource( url, name ) )
        {
            HandleFeed( monitor, c.SecretKeyStore, url, name, creds );
        }

        private protected NuGetFeedBase( NuGetClient c, PackageSource packageSource )
        {
            _lock = new object();
            Client = c;
            PackageSource = packageSource;
        }

        internal readonly PackageSource PackageSource;

        /// <summary>
        /// Associated source feed. Null when this is a INuGetRepository and no feed definition is associated to this repository.
        /// </summary>
        internal INuGetFeed? Feed
        {
            get
            {
                Debug.Assert( _feed != null || this is INuGetRepository );
                return _feed;
            }
        }

        public string Url => PackageSource.Source;

        public string Name => PackageSource.Name;

        internal INuGetFeed HandleFeed( IActivityMonitor monitor, SecretKeyStore keyStore, string url, string name, SimpleCredentials? creds )
        {
            Debug.Assert( _feed == null && url.Equals( Url, StringComparison.OrdinalIgnoreCase ) );
            if( creds?.IsSecretKeyName == true )
            {
                keyStore.DeclareSecretKey( creds.PasswordOrSecretKeyName, current => current?.Description
                                    ?? $"Used to enable CKli to retrieve informations about NuGet packages from feed '{name}' and to configure NuGet.config file." );
            }
            return _feed = new ReadFeed( this, name, creds );
        }

        protected async Task<T> SafeCallAsync<TResource, T>( IActivityMonitor monitor,
                                                            Func<SourceRepository, TResource, NuGetLoggerAdapter, Task<T>> f ) where TResource : class, INuGetResource
        {
            bool retry = false;
            var logger = new NuGetLoggerAdapter( monitor );
            if( _sourceRepository == null )
            {
                lock( _lock )
                {
                    if( _sourceRepository == null )
                    {
                        _sourceRepository = new SourceRepository( PackageSource, NuGetClient.StaticProviders );
                    }
                }
            }
        again:
            TResource? meta = null;
            try
            {
                meta = await _sourceRepository.GetResourceAsync<TResource>();
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

        private protected abstract bool CanRetry( INuGetResource meta, NuGetLoggerAdapter logger, Exception ex );
    }
}
