using CK.Core;
using CK.Env;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.NPM
{
    public class NPMClient : IArtifactRepositoryFactory
    {
        /// <summary>
        /// Exposes the "NPM" <see cref="ArtifactType"/>. This type of artifact is installable.
        /// </summary>
        public static readonly ArtifactType NPMType = ArtifactType.Register( "NPM", true );

        readonly Dictionary<string, INPMFeed> _feeds;
        readonly IActivityMonitor _activityMonitor;

        public NPMClient( HttpClient httpClient, ISecretKeyStore keyStore, IActivityMonitor activityMonitor )
        {
            HttpClient = httpClient;
            SecretKeyStore = keyStore;
            _activityMonitor = activityMonitor;
            _feeds = new Dictionary<string, INPMFeed>();
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
        public IArtifactRepository Find( string uniqueRepositoryName )
        {
            _feeds.TryGetValue( uniqueRepositoryName, out var f );
            return f;
        }

        /// <summary>
        /// Finds or creates a feed.
        /// If a feed with the same <see cref="INPMFeedInfo.Name"/> exists,
        /// it is returned.
        /// </summary>
        /// <param name="info">The feed info.</param>
        /// <returns>The new or existing feed.</returns>
        public INPMFeed FindOrCreate( INPMFeedInfo info )
        {
            if( !_feeds.TryGetValue( info.UniqueArtifactRepositoryName, out var feed ) )
            {
                string pat = SecretKeyStore.GetSecretKey( _activityMonitor, info.SecretKeyName, true );
                switch( info )
                {
                    case NPMAzureFeedInfo a: feed = new NPMClientAzureFeed( this, a, pat ); break;
                    case NPMStandardFeedInfo s: feed = new NPMClientStandardFeed( this, s, pat ); break;
                    default: throw new ArgumentException( $"Unhandled type: {info}", nameof( info ) );
                }
                _feeds.Add( info.UniqueArtifactRepositoryName, feed );
            }
            return feed;
        }

        IArtifactRepositoryInfo IArtifactRepositoryFactory.CreateInfo( in XElementReader r )
        {
            return NPMFeedInfo.Create( r, skipMissingType: true );
        }

        IArtifactRepository IArtifactRepositoryFactory.FindOrCreate( IActivityMonitor m, IArtifactRepositoryInfo info )
        {
            if( info == null ) throw new ArgumentNullException( nameof( info ) );
            if( !(info is INPMFeedInfo fInfo) ) return null;
            return FindOrCreate( fInfo );
        }
    }
}
