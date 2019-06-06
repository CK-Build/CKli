using CK.Env;
using CK.Core;
using System.Net.Http;
using CK.Env.NPM;

namespace CKli
{
    public class XNPMClient : XTypedObject
    {
        readonly NPMClient _npmClient;

        public XNPMClient(
            HttpClient sharedHttpClient,
            ISecretKeyStore secretKeyStore,
            ArtifactCenter artifact,
            IEnvLocalFeedProvider localFeedProvider,
            FileSystem fs,
            Initializer initializer)
            : base( initializer )
        {
            _npmClient = new NPMClient( sharedHttpClient, secretKeyStore, initializer.Monitor );
            artifact.Register( _npmClient );
            fs.ServiceContainer.Add( _npmClient );
            localFeedProvider.Register( new EnvLocalFeedProviderNPMHandler() );
            initializer.Services.Add( this );
        }

    }
}
