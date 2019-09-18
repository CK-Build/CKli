using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using CK.Core;

namespace CK.Env
{
    /// <summary>
    /// Registers <see cref="IArtifactTypeHandler"/> and their created <see cref="IArtifactRepository"/>
    /// and <see cref="IArtifactFeed"/>.
    /// </summary>
    public class ArtifactCenter
    {
        readonly List<IArtifactTypeHandler> _typeHandlers;
        readonly List<IArtifactFeed> _feeds;
        readonly List<IArtifactRepository> _repositories;

        readonly Dictionary<Artifact, IReadOnlyCollection<ArtifactAvailableInstances>> _cachedArtifacts;

        public ArtifactCenter()
        {
            _typeHandlers = new List<IArtifactTypeHandler>();
            _feeds = new List<IArtifactFeed>();
            _repositories = new List<IArtifactRepository>();
            _cachedArtifacts = new Dictionary<Artifact, IReadOnlyCollection<ArtifactAvailableInstances>>();
        }

        /// <summary>
        /// Registers a new <see cref="IArtifactTypeHandler"/>.
        /// </summary>
        /// <param name="factory">The factory to register.</param>
        public void Register( IArtifactTypeHandler factory )
        {
            if( factory == null ) throw new ArgumentNullException( nameof( factory ) );
            if( _typeHandlers.Contains( factory ) ) throw new InvalidOperationException( nameof( factory ) );
            _typeHandlers.Add( factory );
        }

        public void Initialize( IActivityMonitor m, IEnumerable<XElementReader> feeds, IEnumerable<XElementReader> repositories )
        {
            foreach( var r in repositories )
            {
                InstanciateRepository( r );
            }
            foreach( var r in feeds )
            {
                InstanciateFeed( r );
            }
        }

        IArtifactRepository InstanciateRepository( in XElementReader r )
        {
            IArtifactRepository repo = null;
            foreach( var f in _typeHandlers )
            {
                repo = f.CreateRepository( r );
                if( repo != null )
                {
                    // First, check naming coherency.
                    var checkName = r.HandleOptionalAttribute<string>( "CheckName", null );
                    if( checkName != null && checkName != repo.UniqueRepositoryName )
                    {
                        r.ThrowXmlException( $"Invalid check for name: CheckName is '{checkName}' but the actual repository name is '{repo.UniqueRepositoryName}'." );
                    }
                    var checkSecretKeyName = r.HandleOptionalAttribute<string>( "CheckSecretKeyName", null );
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

        IArtifactFeed InstanciateFeed( XElementReader r )
        {
            foreach( var h in _typeHandlers )
            {
                var f = h.CreateFeed( r, _repositories, _feeds );
                if( f != null )
                {
                    if( _feeds.Any( feed => feed.TypedName == f.TypedName ) )
                    {
                        r.ThrowXmlException( $"Repository '{f.TypedName}' already exists. " );
                    }
                    else
                    {
                        _feeds.Add( f );
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
        /// Attempts to resolve required secrets for a set of <see cref="IArtifactRepository"/>.
        /// If a secret can not be resolved, it will appear as null in the result list.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="repositories">The set of repository for which secrets must be resolved.</param>
        /// <param name="allMustBeResolved">
        /// True to return null if at least one secret is not resolved and to log an error for each of them.
        /// When false, the non null returned list can contain null secret.
        /// </param>
        /// <returns>
        /// The list of resolved secrets: a null secret means that the secret has not been successfully obtained
        /// for the corresponding <see cref="IArtifactRepositoryInfo.SecretKeyName"/>.
        /// </returns>
        public List<(string SecretKeyName, string Secret)> ResolveSecrets(
            IActivityMonitor m,
            IEnumerable<IArtifactRepository> repositories,
            bool allMustBeResolved )
        {
            var r = repositories.Where( feed => !String.IsNullOrWhiteSpace( feed.SecretKeyName ) )
                               .GroupBy( feed => feed.SecretKeyName )
                               .Select( g => (g.Key, Secret: g.First().ResolveSecret( m )) )
                               .ToList();
            if( allMustBeResolved )
            {
                bool missing = false;
                foreach( var (SecretKeyName, Secret) in r )
                {
                    if( Secret == null )
                    {
                        m.Error( $"A required repository secret is missing: {SecretKeyName}" );
                        missing = true;
                    }
                }
                if( missing ) r = null;
            }
            return r;
        }

        /// <summary>
        /// Returns a collection of <see cref="ArtifactAvailableInstances"/> for an <see cref="Artifact"/> accross all feeds.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="artifact">The artifact top lookup.</param>
        /// <returns>The collection of available instances accross all feeds.</returns>
        public IReadOnlyCollection<ArtifactAvailableInstances> GetExternalVersions( IActivityMonitor m, Artifact artifact )
        {
            if( !_cachedArtifacts.TryGetValue( artifact, out var instances ) )
            {
                // Don't use parallel resolution here because of the monitor (and parallel here is quite useless).
                var result = new List<ArtifactAvailableInstances>();
                foreach( var f in _feeds.Where( f => f.ArtifactType == artifact.Type ) )
                {
                    var available = f.GetVersionsAsync( m, artifact.Name ).GetAwaiter().GetResult();
                    result.Add( available );
                }
                _cachedArtifacts.Add( artifact, instances = result );
            }
            return instances;
        }
    }
}
