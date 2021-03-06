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
        class ReadFeed : INuGetFeed, IPackageFeed
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
                INuGetRepository? repo = _baseFeed as INuGetRepository;
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

            public Task<ArtifactAvailableInstances> GetVersionsAsync( IActivityMonitor m, string artifactName )
            {
                return _baseFeed.SafeCall<MetadataResource,ArtifactAvailableInstances>( m, ( sources, meta, logger ) => GetAvailable( meta, logger, artifactName ) );
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

            public Task<IPackageInfo?> GetPackageInfoAsync( IActivityMonitor m, ArtifactInstance instance )
            {
                return _baseFeed.SafeCall<DependencyInfoResource, IPackageInfo?>( m, ( sources, meta, logger ) => GetPackageInfo( meta, logger, instance, default ) );
            }

            class PackageInfoReader
            {
                static readonly Type _tResolver;
                static readonly MethodInfo _mGetDependencies;
                static readonly FieldInfo _fClient;
                static readonly FieldInfo _fRegResource;

                static PackageInfoReader()
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

                public PackageInfoReader( DependencyInfoResource meta, SourceCacheContext sourceCache )
                {
                    // Check reflection here: avoid type initialization error.
                    CheckReflection();
                    CurrentInfoResource = meta;
                    _sourceCache = sourceCache;
                    _regResource = (RegistrationResourceV3)_fRegResource.GetValue( meta )!;
                    _client = (HttpSource)_fClient.GetValue( meta )!;
                }

                public async Task<IPackageInfo?> GetPackageInfoAsync( NuGetLoggerAdapter logger, ArtifactInstance instance, CancellationToken token )
                { 
                    var packageId = instance.Artifact.Name;
                    var version = NuGetVersion.Parse( instance.Version.ParsedText );
                    var singleVersion = new VersionRange( minVersion: version, includeMinVersion: true, maxVersion: version, includeMaxVersion: true );
                    // Construct the registration index url
                    var uri = _regResource.GetUri( instance.Artifact.Name );
                    var param = new object[] { _client, uri, packageId, singleVersion, _sourceCache, logger, token };
                    var o = await ((Task<IEnumerable<RemoteSourceDependencyInfo>>)_mGetDependencies.Invoke( null, param )!);
                    if( o == null || !o.Any() ) return null;
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
                        return null;
                    }
                    var result = new PackageInfo();
                    result.Key = instance;
                    var savors = NuGetClient.Savors.EmptyTrait;
                    foreach( var d in deps.DependencyGroups )
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

            }

            PackageInfoReader? _packageReader;

            Task<IPackageInfo?> GetPackageInfo( DependencyInfoResource meta, NuGetLoggerAdapter logger, ArtifactInstance instance, CancellationToken token )
            {
                if( _packageReader == null || _packageReader.CurrentInfoResource != meta )
                {
                    _packageReader = new PackageInfoReader( meta, _baseFeed.Client.SourceCache );
                }
                return _packageReader.GetPackageInfoAsync( logger, instance, token );
            }

            public override string ToString() => TypedName;
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

        protected async Task<T> SafeCall<TResource,T>( IActivityMonitor m, Func<SourceRepository, TResource, NuGetLoggerAdapter, Task<T>> f ) where TResource : class, INuGetResource
        {
            bool retry = false;
            var logger = new NuGetLoggerAdapter( m );
            if( _sourceRepository == null )
            {
                _sourceRepository = new SourceRepository( PackageSource, NuGetClient.StaticProviders );
            }
        again:
            TResource meta = null;
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
