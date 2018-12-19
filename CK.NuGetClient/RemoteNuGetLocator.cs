using CK.Env;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.NuGetClient
{
    class RemoteNuGetLocator : IArtifactLocator
    {
        readonly NuGetRemoteFeedBase _feed;

        public RemoteNuGetLocator( ArtifactInstance a, NuGetRemoteFeedBase feed )
        {
            Instance = a;
            _feed = feed;
        }

        public ArtifactInstance Instance { get; }
    }
}
