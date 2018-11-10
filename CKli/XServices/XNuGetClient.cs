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
        NuGetClient _nuGetClient;

        public XNuGetClient(
            XSharedHttpClient sharedHttpClient,
            XSecretKeyStore secretKeyStore,
            Initializer initializer )
            : base( initializer )
        {
            _sharedHttpClient = sharedHttpClient;
            _secretKeyStore = secretKeyStore;

            initializer.Services.Add( this );
        }

        public INuGetClient NuGetClient => _nuGetClient ?? (_nuGetClient = new NuGetClient( _sharedHttpClient.Shared, _secretKeyStore ));       

        void IDisposable.Dispose()
        {
            if( _nuGetClient != null ) _nuGetClient.Dispose();
        }
    }
}
