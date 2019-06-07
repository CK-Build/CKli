using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CK.Core;

namespace CK.Env.NPM
{
    class NPMFeed : INPMFeed
    {
        internal NPMFeed( string scope, string url, SimpleCredentials creds )
        {
            Scope = scope;
            Url = url;
            Credentials = creds;
            TypedName = $"{NPMClient.NPMType.Name}:{scope}";
        }

        public string Scope { get; }

        public string Url { get; }

        public SimpleCredentials Credentials { get; }

        public string TypedName { get; }

        public ArtifactType ArtifactType => throw new NotImplementedException();

        public Task<IReadOnlyCollection<ArtifactAvailableInstances>> GetVersionsAsync( IActivityMonitor m, IEnumerable<string> artifactNames )
        {
            throw new NotImplementedException();
        }
    }
}
