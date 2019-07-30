using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CSemVer;
using Newtonsoft.Json.Linq;

namespace CK.Env.NPM
{
    class NPMFeed : INPMFeed
    {
        readonly NPMClient _client;
        readonly NPMRepositoryBase _existingRepository;

        internal NPMFeed(
            NPMClient client,
            string scope,
            string url,
            SimpleCredentials creds,
            NPMRepositoryBase existingRepository )
        {
            _client = client;
            Scope = scope;
            Url = url;
            Credentials = creds;
            TypedName = $"{NPMClient.NPMType.Name}:{scope}";
            _existingRepository = existingRepository;
        }

        string IArtifactFeedIdentity.Name => Scope;

        public string Scope { get; }

        public string Url { get; }

        public SimpleCredentials Credentials { get; }

        public string TypedName { get; }

        public ArtifactType ArtifactType => NPMClient.NPMType;

        public async Task<ArtifactAvailableInstances> GetVersionsAsync( IActivityMonitor m, string artifactName )
        {
            if( _existingRepository == null )
            {
                throw new NotImplementedException( "We must build a new Registry if the feed does not correspond to an existing repository." );
            }
            var registry = _existingRepository.GetRegistry( m );
            var v = new ArtifactAvailableInstances( this, artifactName );

            var result = await registry.View( m, artifactName );
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
                    else v = v.WithVersion( sV );
                }
            }
            return v;
        }
    }
}
