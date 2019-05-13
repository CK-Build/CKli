using CK.Env;
using CK.Core;
using System.Net.Http;

namespace CKli
{
    public class XCKSetupClient : XTypedObject
    {
       readonly CKSetupClient _client;

        public XCKSetupClient(
            HttpClient sharedHttpClient,
            ISecretKeyStore secretKeyStore,
            ArtifactCenter artifact,
            FileSystem fs,
            Initializer initializer )
            : base( initializer )
        {
            _client = new CKSetupClient( secretKeyStore, sharedHttpClient );
            fs.ServiceContainer.Add( _client );
            artifact.Add( _client );
            initializer.Services.Add( this );
        }

        public CKSetupClient CKSetupClient => _client;       

    }
}
