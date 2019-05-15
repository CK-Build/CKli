using CK.Env;
using System;
using CK.Core;
using CK.Env.NuGet;
using System.Net.Http;

namespace CKli
{
    public class XNuGetClient : XTypedObject, IDisposable
    {
        readonly NuGetClient _nuGetClient;

        public XNuGetClient(
            HttpClient sharedHttpClient,
            ISecretKeyStore secretKeyStore,
            ArtifactCenter artifact,
            IEnvLocalFeedProvider localFeedProvider,
            FileSystem fs,
            Initializer initializer )
            : base( initializer )
        {
            _nuGetClient = new NuGetClient( sharedHttpClient, secretKeyStore );
            localFeedProvider.Register( new EnvLocalFeedProviderNuGetHandler() );
            fs.ServiceContainer.Add( _nuGetClient );
            artifact.Register( _nuGetClient );
           initializer.Services.Add( this );
        }

        void IDisposable.Dispose()
        {
            _nuGetClient.Dispose();
        }
    }
}
