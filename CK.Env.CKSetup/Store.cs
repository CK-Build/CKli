using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CK.Core;
using CK.Text;
using CKSetup;

namespace CK.Env
{
    class Store : IArtifactRepository
    {
        readonly HttpClient _sharedHttpClient;
        readonly ISecretKeyStore _keyStore;

        public Store( ICKSetupStoreInfo info, ISecretKeyStore keyStore, HttpClient sharedHttpClient )
        {
            _sharedHttpClient = sharedHttpClient;
            _keyStore = keyStore;
            Info = info;
        }

        public ICKSetupStoreInfo Info { get; }

        IArtifactRepositoryInfo IArtifactRepository.Info => Info;

        public string SecretKeyName => Info.SecretKeyName;

        public string ResolveSecret( IActivityMonitor m, bool throwOnEmpty = false )
        {
            var s = Info.SecretKeyName;
            return String.IsNullOrWhiteSpace( s )
                    ? null
                    : _keyStore.GetSecretKey( m, SecretKeyName, throwOnEmpty, $"Required to push to {Info.Name}." );
        }

        public bool HandleArtifactType( in ArtifactType artifactType ) => artifactType == CKSetupClient.CKSetupType;

        public Task<bool> PushAsync( IActivityMonitor m, IArtifactLocalSet artifacts )
        {
            bool success = true;
            using( m.OnError( () => success = false ) )
            {
                if( !(artifacts is CKSetupArtifactLocalSet local) )
                {
                    m.Error( $"Invalid artifact local set for CKSetup store." );
                    success = false;
                }
                else
                {
                    var secret = ResolveSecret( m, true );
                    var all = new HashSet<ComponentRef>( local.ComponentRefs );
                    using( LocalStore store = LocalStore.OpenOrCreate( m, local.StorePath ) )
                    {
                        success &= store != null && store.PushComponents( c => all.Remove( c.GetRef() ), Info.Url, secret );
                    }
                    if( all.Count > 0 )
                    {
                        m.Error( $"Local store '{local.StorePath}' does not contain CKSetup components: ${all.Select( c => c.ToString() ).Concatenate()}." );
                        success = false;
                    }
                }
            }
            return Task.FromResult( success );
        }
    }
}
