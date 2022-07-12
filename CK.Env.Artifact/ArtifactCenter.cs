using CK.Core;
using CK.Build;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using System.IO;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using CK.Build.PackageDB;

namespace CK.Env
{
    /// <summary>
    /// Registers <see cref="IArtifactTypeHandler"/> and their created <see cref="IArtifactRepository"/>
    /// and <see cref="IArtifactFeed"/>.
    /// <para>
    /// TODO: refactor with a Base that allows one specialization to be initialized
    /// like the current one (ctor + Register + Initialize) for CKli and a cleaner one (ctor only + register via DI of IEnumerable of multiple services)
    /// that can Add/Remove repository and feeds.
    /// Locks should be used. Currently ctor + Register + Initialize must be done sequentially.
    /// Except this, this class is thread safe.
    /// </para>
    /// </summary>
    public class ArtifactCenter
    {
        readonly List<IArtifactTypeHandler> _typeHandlers;
        readonly List<IArtifactFeed> _feeds;
        readonly List<IArtifactRepository> _repositories;
        readonly NormalizedPath _packageCacheFilePath;

        LivePackageCache? _packageCache;

        public ArtifactCenter( NormalizedPath packageCacheFilePath )
        {
            _typeHandlers = new List<IArtifactTypeHandler>();
            _feeds = new List<IArtifactFeed>();
            _repositories = new List<IArtifactRepository>();
            _packageCacheFilePath = packageCacheFilePath;
        }

        /// <summary>
        /// Registers a new <see cref="IArtifactTypeHandler"/>.
        /// </summary>
        /// <param name="factory">The factory to register.</param>
        public void Register( IArtifactTypeHandler factory )
        {
            Throw.CheckNotNullArgument( factory );
            Throw.CheckState( !_typeHandlers.Contains( factory ) );
            _typeHandlers.Add( factory );
        }

        public void Initialize( IActivityMonitor monitor, IEnumerable<XElementReader> feeds, IEnumerable<XElementReader> repositories )
        {
            // First creates the repositories.
            foreach( var r in repositories )
            {
                InstantiateRepository( monitor, r );
            }
            // And then the feeds since a feed can be based on its corresponding repository.
            foreach( var r in feeds )
            {
                InstantiateFeed( monitor, r );
            }
            _packageCache = LivePackageCache.LoadOrCreate( monitor, _packageCacheFilePath, _feeds.OfType<IPackageFeed>() );
        }

        IArtifactRepository InstantiateRepository( IActivityMonitor monitor, in XElementReader r )
        {
            IArtifactRepository? repo = null;
            foreach( var f in _typeHandlers )
            {
                repo = f.CreateRepositoryFromXml( monitor, r );
                if( repo != null )
                {
                    // First, check naming coherency.
                    var checkName = r.HandleOptionalAttribute<string?>( "CheckName", null );
                    if( checkName != null && checkName != repo.UniqueRepositoryName )
                    {
                        r.ThrowXmlException( $"Invalid check for name: CheckName is '{checkName}' but the actual repository name is '{repo.UniqueRepositoryName}'." );
                    }
                    var checkSecretKeyName = r.HandleOptionalAttribute<string?>( "CheckSecretKeyName", null );
                    if( checkSecretKeyName != null && checkSecretKeyName != repo.SecretKeyName )
                    {
                        r.ThrowXmlException( $"Invalid check for secret key name: CheckSecretKeyName is '{checkSecretKeyName}' but the actual repository secret key name is '{repo.SecretKeyName}'." );
                    }
                    // Second, check for duplicates.
                    if( _repositories.Any( rp => rp.UniqueRepositoryName == repo.UniqueRepositoryName ) )
                    {
                        r.ThrowXmlException( $"Repository '{repo.UniqueRepositoryName}' already exists. " );
                    }
                    else
                    {
                        _repositories.Add( repo );
                    }
                    r.WarnUnhandled();
                    break;
                }
            }
            if( repo == null ) r.ThrowXmlException( "Unable to map Xml element to an Artifact repository." );
            return repo;
        }

        IArtifactFeed InstantiateFeed( IActivityMonitor monitor, XElementReader r )
        {
            foreach( var h in _typeHandlers )
            {
                var f = h.CreateFeedFromXml( monitor, r, _repositories, _feeds );
                if( f != null )
                {
                    if( _feeds.Any( feed => feed.TypedName == f.TypedName ) )
                    {
                        r.ThrowXmlException( $"Repository '{f.TypedName}' already exists. " );
                    }
                    else
                    {
                        _feeds.Add( f );
                        f.ConfigureCredentials( monitor );
                    }
                    r.WarnUnhandled();
                    return f;
                }
            }
            r.ThrowXmlException( "Unable to resolve a package feed." );
            return null;
        }

        /// <summary>
        /// Gets the available feeds.
        /// </summary>
        public IReadOnlyCollection<IArtifactFeed> Feeds => _feeds;

        /// <summary>
        /// Gets the available repositories.
        /// </summary>
        public IReadOnlyCollection<IArtifactRepository> Repositories => _repositories;

        /// <summary>
        /// Gets the live package cache.
        /// <see cref="Initialize(IActivityMonitor, IEnumerable{XElementReader}, IEnumerable{XElementReader})"/> MUST have been called
        /// before using this.
        /// </summary>
        public LivePackageCache LiveCache => _packageCache!;

        /// <summary>
        /// Attempts to resolve required secrets for a set of <see cref="IArtifactRepository"/>.
        /// If a secret can not be resolved, it will appear as null in the result list.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="repositories">The set of repository for which secrets must be resolved.</param>
        /// <param name="allMustBeResolved">
        /// True to return null if at least one secret is not resolved and to log an error for each of them.
        /// When false, the non null returned list can contain null secret.
        /// </param>
        /// <returns>
        /// The list of resolved secrets: a null secret means that the secret has not been successfully obtained
        /// for the corresponding <see cref="IArtifactRepositoryInfo.SecretKeyName"/>. If <paramref name="allMustBeResolved"/>
        /// is true and one secret is missing, this returned list is null.
        /// </returns>
        public List<(string SecretKeyName, string? Secret)>? ResolveSecrets( IActivityMonitor monitor,
                                                                             IEnumerable<IArtifactRepository> repositories,
                                                                             bool allMustBeResolved )
        {
            List<(string Key, string? Secret)>? r = repositories.Where( feed => !String.IsNullOrWhiteSpace( feed.SecretKeyName ) )
                                                                .GroupBy( feed => feed.SecretKeyName )
                                                                .Select( g => (g.Key!, Secret: g.First().ResolveSecret( monitor )) )
                                                                .ToList();
            if( allMustBeResolved )
            {
                bool missing = false;
                foreach( var (SecretKeyName, Secret) in r )
                {
                    if( Secret == null )
                    {
                        monitor.Error( $"A required repository secret is missing: {SecretKeyName}" );
                        missing = true;
                    }
                }
                if( missing ) r = null;
            }
            return r;
        }

        /// <summary>
        /// Returns a collection of <see cref="ArtifactAvailableInstances"/> for an <see cref="Artifact"/> across all feeds.
        /// Each ArtifactAvailableInstances can contain up to 5 versions that are the best for each of the 5 <see cref="CSemVer.PackageQuality"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="artifact">The artifact to lookup.</param>
        /// <param name="requestFeeds">
        /// True to request the <see cref="LivePackageCache.Feeds"/>.
        /// When false, the feeds are requested only if the artifact is totally unknown in the cache.
        /// </param>
        /// <returns>The collection of available instances across all feeds.</returns>
        public async Task<IReadOnlyCollection<ArtifactAvailableInstances>> GetExternalVersionsAsync( IActivityMonitor monitor, Artifact artifact, bool requestFeeds )
        {
            Throw.CheckState( _packageCache != null );
            var result = new List<ArtifactAvailableInstances>();
            var db = _packageCache.Cache.DB;
            if( !requestFeeds )
            {
                var gotThem = db.GetAvailableVersions( artifact );
                if( gotThem.Count > 0 )
                {
                    return gotThem;
                }
                monitor.Trace( $"Artifact available versions for '{artifact}' not found in package cache. Soliciting feeds." );
            }
            foreach( var f in _feeds.Where( f => f.ArtifactType == artifact.Type ) )
            {
                ArtifactAvailableInstances? available = await GetVersionsAsync( monitor, artifact, _packageCache, f );
                if( available != null ) result.Add( available );
            }
            return result;
        }

        static async Task<ArtifactAvailableInstances?> GetVersionsAsync( IActivityMonitor monitor, Artifact artifact, LivePackageCache cache, IArtifactFeed f )
        {
            var available = await f.GetVersionsAsync( monitor, artifact.Name );
            if( available != null )
            {
                foreach( var a in available )
                {
                    try
                    {
                        await cache.EnsureAsync( monitor, a );
                    }
                    catch( Exception ex )
                    {
                        monitor.Error( $"Error while caching '{a}'. Ignoring the error, the instance is not cached.", ex );
                    }
                }
            }
            return available;
        }
    }
}
