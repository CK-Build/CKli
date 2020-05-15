using CK.Core;
using CK.Build;
using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    class NPMFeed : INPMFeed
    {
        readonly Func<Registry> _registryFactory;
        Registry _registry;

        internal NPMFeed(
            string scope,
            string url,
            SimpleCredentials creds,
            Func<Registry> registryFactory )
        {
            Scope = scope;
            Url = url;
            Credentials = creds;
            TypedName = $"{NPMClient.NPMType.Name}:{scope}";
            _registryFactory = registryFactory ?? throw new ArgumentNullException();
        }

        string IArtifactFeedIdentity.Name => Scope;

        public string Scope { get; }

        public string Url { get; }

        public SimpleCredentials Credentials { get; }

        public string TypedName { get; }

        public ArtifactType ArtifactType => NPMClient.NPMType;

        public bool CheckSecret( IActivityMonitor m, bool throwOnMissing = false ) => true;

        public async Task<ArtifactAvailableInstances> GetVersionsAsync( IActivityMonitor m, string artifactName )
        {
            if( _registry == null ) _registry = _registryFactory();
            var v = new ArtifactAvailableInstances( this, artifactName );

            var result = await _registry.View( m, artifactName );
            if( result.exist )
            {
                JObject body = JObject.Parse( result.viewJson );
                var versions = (JObject)body["versions"];
                foreach( var vK in versions )
                {
                    var sV = SVersion.TryParse( vK.Key );
                    if( !sV.IsValid )
                    {
                        m.Warn( $"Unable to parse version '{vK.Key}' for '{artifactName}': {sV.ErrorMessage}" );
                    }
                    else
                    {
                        v = v.WithVersion( sV );
                    }
                }
            }
            return v;
        }

    }
}
