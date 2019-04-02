using CK.Env;
using System;
using CK.Core;
using CK.NuGetClient;

namespace CKli
{
    public class XNuGetClient : XTypedObject, IDisposable
    {
        readonly XSharedHttpClient _sharedHttpClient;
        readonly XCKEnvKeyVault _secretKeyStore;
        readonly NuGetClient _nuGetClient;

        public XNuGetClient(
            XSharedHttpClient sharedHttpClient,
            XCKEnvKeyVault secretKeyStore,
            XArtifactCenter artifact,
            FileSystem fs,
            Initializer initializer )
            : base( initializer )
        {
            _sharedHttpClient = sharedHttpClient;
            _secretKeyStore = secretKeyStore;
            _nuGetClient = new NuGetClient( _sharedHttpClient.Shared, _secretKeyStore );
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
