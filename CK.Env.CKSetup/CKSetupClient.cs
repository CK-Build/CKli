using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CK.Core;
using CKSetup;

namespace CK.Env
{
    public class CKSetupClient : IArtifactRepositoryFactory
    {
        /// <summary>
        /// Exposes the "CKSetup" <see cref="ArtifactType"/>.
        /// This type of artifact is not installable.
        /// </summary>
        public static readonly ArtifactType CKSetupType = ArtifactType.Register( "CKSetup", false );

        readonly HttpClient _sharedHttpClient;
        readonly ISecretKeyStore _keyStore;
        readonly Dictionary<string, Store> _stores;

        public CKSetupClient( ISecretKeyStore keyStore, HttpClient sharedHttpClient )
        {
            _keyStore = keyStore;
            _sharedHttpClient = sharedHttpClient;
            _stores = new Dictionary<string, Store>();
        }

        public IArtifactRepositoryInfo CreateInfo( XElement e )
        {
            string type = (string)e.Attribute( "Type" );
            if( type == "CKSetup" )
            {
                string url = (string)e.Attribute( "Url" );
                if( url == null || url == Facade.DefaultStorePath ) return DefaultPublicStore.Default;

                string name = (string)e.AttributeRequired( "Name" );
                if( !Regex.IsMatch( name, "^\\w+$", RegexOptions.CultureInvariant ) )
                {
                    throw new ArgumentException( $"Invalid name. Must be an identifier ('^\\w+$' regex)." );
                }
                if( name == "Public" ) throw new ArgumentException( $"'Public' name is reserved for the default public store." );
                return new StoreInfo( url, name );
            }
            return null;
        }

        public IArtifactRepository Find( string uniqueRepositoryName )
        {
            _stores.TryGetValue( uniqueRepositoryName, out var store );
            return store;
        }

        public IArtifactRepository FindOrCreate( IActivityMonitor m, IArtifactRepositoryInfo info )
        {
            if( info is ICKSetupStoreInfo s )
            {
                if( !_stores.TryGetValue( s.UniqueArtifactRepositoryName, out var store ) )
                {
                    store = new Store( s, _keyStore, _sharedHttpClient );
                    _stores.Add( s.UniqueArtifactRepositoryName, store );
                }
                return store;
            }
            return null;
        }
    }
}
