using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CK.Core;
using CKSetup;

namespace CK.Env.CKSetup
{
    public class CKSetupClient : IArtifactTypeHandler
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

        public IArtifactRepositoryInfo ReadRepositoryInfo( in XElementReader r )
        {
            string type = r.HandleOptionalAttribute<string>( "Type", null );
            if( type == "CKSetup" )
            {
                string url = r.HandleOptionalAttribute<string>( "Url", null );
                if( url == null || url == Facade.DefaultStorePath ) return DefaultPublicStore.Default;

                string name = r.HandleRequiredAttribute<string>( "Name" );
                if( !Regex.IsMatch( name, "^\\w+$", RegexOptions.CultureInvariant ) )
                {
                    throw new ArgumentException( $"Invalid name. Must be an identifier ('^\\w+$' regex)." );
                }
                if( name == "Public" ) throw new ArgumentException( $"'Public' name is reserved for the default public store." );
                return new StoreInfo( url, name );
            }
            return null;
        }

        public IArtifactRepository FindRepository( string uniqueRepositoryName )
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

        public IArtifactFeed CreateFeed( in XElementReader r ) => null;

        public IArtifactFeed FindFeed( string uniqueTypedName ) => null;
    }
}
