using CK.Core;
using CK.SimpleKeyVault;
using System.Net.Http;

namespace CK.Env.CKSetup
{
    public class XCKSetupClient : XTypedObject
    {
        public XCKSetupClient(
            HttpClient sharedHttpClient,
            SecretKeyStore secretKeyStore,
            ArtifactCenter artifact,
            IEnvLocalFeedProvider localFeedProvider,
            FileSystem fs,
            Initializer initializer )
            : base( initializer )
        {
            Client = new CKSetupClient( secretKeyStore, sharedHttpClient );
            fs.ServiceContainer.Add( Client );
            artifact.Register( Client );
            localFeedProvider.Register( new EnvLocalFeedProviderCKSetupHandler() );
            initializer.Services.Add( this );
        }

        public CKSetupClient Client { get; }

    }
}
