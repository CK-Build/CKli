using CK.Core;
using CK.Env;
using CK.Env.NPM;
using System.Net.Http;

namespace CKli
{
    public class XNPMClient : XTypedObject
    {
        readonly NPMClient _npmClient;

        public XNPMClient(
            HttpClient sharedHttpClient,
            SecretKeyStore secretKeyStore,
            ArtifactCenter artifact,
            IEnvLocalFeedProvider localFeedProvider,
            FileSystem fs,
            Initializer initializer )
            : base( initializer )
        {
            _npmClient = new NPMClient( sharedHttpClient, secretKeyStore );
            artifact.Register( _npmClient );
            fs.ServiceContainer.Add( _npmClient );
            localFeedProvider.Register( new EnvLocalFeedProviderNPMHandler() );
            initializer.Services.Add( this );
        }

    }
}
