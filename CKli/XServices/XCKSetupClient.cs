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
    public class XCKSetupClient : XTypedObject
    {
        readonly XSharedHttpClient _sharedHttpClient;
        readonly XSecretKeyStore _secretKeyStore;
        readonly CKSetupClient _client;

        public XCKSetupClient(
            XSharedHttpClient sharedHttpClient,
            XSecretKeyStore secretKeyStore,
            XArtifactCenter artifact,
            FileSystem fs,
            Initializer initializer )
            : base( initializer )
        {
            _sharedHttpClient = sharedHttpClient;
            _secretKeyStore = secretKeyStore;
            _client = new CKSetupClient( _secretKeyStore, sharedHttpClient.Shared );
            fs.ServiceContainer.Add( _client );
            artifact.ArtifactCenter.Add( _client );
            initializer.Services.Add( this );
        }

        public CKSetupClient CKSetupClient => _client;       

    }
}
