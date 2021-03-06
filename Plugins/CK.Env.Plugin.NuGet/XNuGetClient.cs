using CK.Core;
using CK.Env;
using CK.Env.NuGet;
using CK.SimpleKeyVault;
using System;
using System.Net.Http;

namespace CKli
{
    public class XNuGetClient : XTypedObject, IDisposable
    {
        readonly NuGetClient _nuGetClient;

        public XNuGetClient(
            HttpClient sharedHttpClient,
            SecretKeyStore secretKeyStore,
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
