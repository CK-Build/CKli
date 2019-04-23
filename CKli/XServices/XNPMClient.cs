using CK.Env;
using System;
using CK.Core;
using CK.NuGetClient;
using CK.NPMClient;

namespace CKli
{
    public class XNPMClient : XTypedObject
    {
        readonly XSharedHttpClient _sharedHttpClient;
        readonly NPMClient _npmClient;

        public XNPMClient(
            XSharedHttpClient sharedHttpClient,
            ISecretKeyStore secretKeyStore,
            XArtifactCenter artifact,
            FileSystem fs,
            Initializer initializer)
            : base( initializer )
        {
            _sharedHttpClient = sharedHttpClient;
            _npmClient = new NPMClient( _sharedHttpClient.Shared, secretKeyStore, initializer.Monitor );
            fs.ServiceContainer.Add<INPMClient>( _npmClient );
            artifact.ArtifactCenter.Add( _npmClient );
            initializer.Services.Add( this );
        }

        public INPMClient NPMClient => _npmClient;

    }
}
