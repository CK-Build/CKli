using CK.Env;
using CK.Core;

namespace CKli
{
    public class XCKSetupClient : XTypedObject
    {
        readonly XSharedHttpClient _sharedHttpClient;
        readonly CKSetupClient _client;

        public XCKSetupClient(
            XSharedHttpClient sharedHttpClient,
            ISecretKeyStore secretKeyStore,
            XArtifactCenter artifact,
            FileSystem fs,
            Initializer initializer )
            : base( initializer )
        {
            _sharedHttpClient = sharedHttpClient;
            _client = new CKSetupClient( secretKeyStore, sharedHttpClient.Shared );
            fs.ServiceContainer.Add( _client );
            artifact.ArtifactCenter.Add( _client );
            initializer.Services.Add( this );
        }

        public CKSetupClient CKSetupClient => _client;       

    }
}
