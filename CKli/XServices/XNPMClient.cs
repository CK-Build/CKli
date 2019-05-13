using CK.Env;
using System;
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
            FileSystem fs,
            Initializer initializer)
            : base( initializer )
        {
            _npmClient = new NPMClient( sharedHttpClient, secretKeyStore, initializer.Monitor );
            fs.ServiceContainer.Add( _npmClient );
            artifact.Add( _npmClient );
            initializer.Services.Add( this );
        }

    }
}
