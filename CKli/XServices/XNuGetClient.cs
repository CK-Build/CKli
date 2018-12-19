using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Core;
using System.Net.Http;
using CK.NuGetClient;

namespace CKli
{
    public class XNuGetClient : XTypedObject, IDisposable
    {
        readonly XSharedHttpClient _sharedHttpClient;
        readonly XSecretKeyStore _secretKeyStore;
        readonly NuGetClient _nuGetClient;

        public XNuGetClient(
            XSharedHttpClient sharedHttpClient,
            XSecretKeyStore secretKeyStore,
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
