using CK.Env;
using System;
using CK.Core;
using CK.NuGetClient;

namespace CKli
{
    public class XNuGetClient : XTypedObject, IDisposable
    {
        readonly XSharedHttpClient _sharedHttpClient;
        readonly NuGetClient _nuGetClient;

        public XNuGetClient(
            XSharedHttpClient sharedHttpClient,
            ISecretKeyStore secretKeyStore,
            XArtifactCenter artifact,
            FileSystem fs,
            Initializer initializer )
            : base( initializer )
        {
            _sharedHttpClient = sharedHttpClient;
            _nuGetClient = new NuGetClient( _sharedHttpClient.Shared, secretKeyStore );
            fs.ServiceContainer.Add<INuGetClient>( _nuGetClient );
            artifact.ArtifactCenter.Add( _nuGetClient );
            initializer.Services.Add( this );
        }

        public INuGetClient NuGetClient => _nuGetClient;

        void IDisposable.Dispose()
        {
            _nuGetClient.Dispose();
        }
    }
}
