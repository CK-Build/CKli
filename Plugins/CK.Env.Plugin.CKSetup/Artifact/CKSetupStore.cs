using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CK.Core;
using CK.Text;
using CKSetup;
using CSemVer;

namespace CK.Env.CKSetup
{
    public class CKSetupStore : IArtifactRepository
    {
        readonly HttpClient _sharedHttpClient;
        readonly SecretKeyStore _keyStore;

        public CKSetupStore(
            SecretKeyStore keyStore,
            HttpClient sharedHttpClient,
            Uri url,
            string name )
        {
            _sharedHttpClient = sharedHttpClient;
            _keyStore = keyStore;
            Url = url;
            Name = name;
            SecretKeyName = name != "Public"
                            ? $"CKSETUPREMOTESTORE_{name.ToUpperInvariant()}_PUSH_API_KEY"
                            : "CKSETUPREMOTESTORE_PUSH_API_KEY"; 
            UniqueRepositoryName = CKSetupClient.CKSetupType.Name + ':' + name;
            _keyStore.DeclareSecretKey( SecretKeyName, current => current?.Description ?? $"Required to push to '{UniqueRepositoryName}'." );
        }

        public CKSetupStore( SecretKeyStore keyStore, HttpClient sharedHttpClient )
            : this( keyStore, sharedHttpClient, Facade.DefaultStoreUrl, "Public" )
        {
        }

        public string Name { get; }

        public Uri Url { get; }

        public string SecretKeyName { get; }

        public PackageQualityFilter QualityFilter { get; }

        public string UniqueRepositoryName { get; }

        public string ResolveSecret( IActivityMonitor m, bool throwOnEmpty = false )
        {
            return _keyStore.GetSecretKey( m, SecretKeyName, throwOnEmpty );
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
                    var all = new HashSet<ComponentRef>( local.ComponentRefs.Where( c => QualityFilter.Accepts( c.Version.PackageQuality ) ) );
                    if( all.Count == 0 )
                    {
                        m.Info( $"No packages accepted by {QualityFilter} filter for {UniqueRepositoryName}." );
                    }
                    else
                    {
                        var secret = ResolveSecret( m, true );
                        using( LocalStore store = LocalStore.OpenOrCreate( m, local.StorePath ) )
                        {
                            success &= store != null && store.PushComponents( c => all.Remove( c.GetRef() ), Url, secret );
                        }
                        if( all.Count > 0 )
                        {
                            m.Error( $"Local store '{local.StorePath}' does not contain CKSetup components: ${all.Select( c => c.ToString() ).Concatenate()}." );
                            success = false;
                        }
                    }
                }
            }
            return Task.FromResult( success );
        }
    }
}
