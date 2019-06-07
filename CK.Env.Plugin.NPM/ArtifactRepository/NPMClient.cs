using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Xml.Linq;

namespace CK.Env.NPM
{
    public class NPMClient : IArtifactTypeHandler
    {
        /// <summary>
        /// Exposes the "NPM" <see cref="ArtifactType"/>. This type of artifact is installable.
        /// </summary>
        public static readonly ArtifactType NPMType = ArtifactType.Register( "NPM", true );

        readonly Dictionary<string, INPMArtifactRepository> _repositories;
        readonly List<NPMFeed> _feeds;

        public NPMClient( HttpClient httpClient, ISecretKeyStore keyStore )
        {
            HttpClient = httpClient;
            SecretKeyStore = keyStore;
            _repositories = new Dictionary<string, INPMArtifactRepository>();
            _feeds = new List<NPMFeed>();
        }

        /// <summary>
        /// Gets the key store.
        /// </summary>
        public ISecretKeyStore SecretKeyStore { get; }

        /// <summary>
        /// Gets the shared <see cref="HttpClient"/> that will be used for remote access.
        /// </summary>
        public HttpClient HttpClient { get; }

        /// <summary>
        /// Finds an already created repository.
        /// </summary>
        /// <param name="uniqueRepositoryName">The <see cref="IArtifactRepositoryInfo.UniqueArtifactRepositoryName"/>.</param>
        /// <returns>The repository or null.</returns>
        public IArtifactRepository FindRepository( string uniqueRepositoryName ) => _repositories.GetValueWithDefault( uniqueRepositoryName, null );

        /// <summary>
        /// Finds or creates a feed.
        /// If a feed with the same <see cref="INPMArtifactRepositoryInfo.Name"/> exists,
        /// it is returned.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="info">The feed info.</param>
        /// <returns>The new or existing feed.</returns>
        public INPMArtifactRepository FindOrCreate( IActivityMonitor m, INPMArtifactRepositoryInfo info )
        {
            if( !_repositories.TryGetValue( info.UniqueArtifactRepositoryName, out var feed ) )
            {
                string pat = SecretKeyStore.GetSecretKey( m, info.SecretKeyName, true );
                switch( info )
                {
                    case NPMAzureFeedInfo a: feed = new NPMClientAzureFeed( this, a, pat ); break;
                    case NPMStandardFeedInfo s: feed = new NPMClientStandardFeed( this, s, pat ); break;
                    default: throw new ArgumentException( $"Unhandled type: {info}", nameof( info ) );
                }
                _repositories.Add( info.UniqueArtifactRepositoryName, feed );
            }
            return feed;
        }

        IArtifactRepositoryInfo IArtifactTypeHandler.ReadRepositoryInfo( in XElementReader r )
        {
            return NPMArtifactRepositoryInfo.Create( r, skipMissingType: true );
        }

        IArtifactRepository IArtifactTypeHandler.FindOrCreate( IActivityMonitor m, IArtifactRepositoryInfo info )
        {
            if( info == null ) throw new ArgumentNullException( nameof( info ) );
            if( !(info is INPMArtifactRepositoryInfo fInfo) ) return null;
            return FindOrCreate( m, fInfo );
        }

        public IArtifactFeed CreateFeed( in XElementReader r )
        {
            if( r.HandleOptionalAttribute<string>( "Type", null ) != NPMType.Name ) return null;
            var url = r.HandleRequiredAttribute<string>( "Url" );
            var scope = r.HandleRequiredAttribute<string>( "Scope" );
            if( !scope.StartsWith( "@" ) ) r.ThrowXmlException( $"Scope attribute must start with @." );
            var xCreds = r.Element.Element( "Credentials" );
            var creds = xCreds != null ? new SimpleCredentials( r.WithElement( xCreds ) ) : null;
            r.WarnUnhandled();
            if( _feeds.Any( f => f.Scope == scope ) ) r.ThrowXmlException( $"NPM feed with the same scope '{scope}' is already defined." );
            if( _feeds.Any( f => StringComparer.OrdinalIgnoreCase.Equals( f.Url, url ) ) ) r.ThrowXmlException( $"NPM feed with the same url '{url}' is already defined." );
            var feed = new NPMFeed( scope, url, creds );
            _feeds.Add( feed );
            return feed;
        }

        public IArtifactFeed FindFeed( string uniqueTypedName )
        {
            return _feeds.SingleOrDefault( f => f.TypedName == uniqueTypedName );
        }
    }
}
