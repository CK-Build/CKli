using CK.Core;
using CK.Env;
using CK.Text;
using Npm.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.NPMClient
{
    public class NPMClient : INPMClient
    {
        readonly Dictionary<string, INPMFeed> _feeds;
        readonly IActivityMonitor _activityMonitor;

        public NPMClient( HttpClient httpClient, ISecretKeyStore keyStore, IActivityMonitor activityMonitor )
        {
            HttpClient = httpClient;
            SecretKeyStore = keyStore;
            _activityMonitor = activityMonitor;
            _feeds = new Dictionary<string, INPMFeed>();
        }

        public ISecretKeyStore SecretKeyStore { get; }

        public HttpClient HttpClient { get; }

        public IArtifactRepository Find( string uniqueRepositoryName )
        {
            _feeds.TryGetValue( uniqueRepositoryName, out var f );
            return f;
        }

        public INPMFeed FindOrCreate(INPMFeedInfo info )
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

        IArtifactRepositoryInfo IArtifactTypeFactory.CreateInfo( XElement e )
        {
            return NPMFeedInfo.Create( e, skipMissingType: true );
        }

        IArtifactRepository IArtifactTypeFactory.FindOrCreate( IActivityMonitor m, IArtifactRepositoryInfo info )
        {
            if( info == null ) throw new ArgumentNullException( nameof( info ) );
            if( !(info is INPMFeedInfo fInfo) ) return null;
            return FindOrCreate( fInfo );
        }
    }
}
