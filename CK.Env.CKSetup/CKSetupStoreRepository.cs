using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CSemVer;

namespace CK.Env
{
    public class CKSetupStoreRepository : IArtifactRepository
    {
        public IArtifactRepositoryInfo Info => throw new NotImplementedException();

        public string SecretKeyName => throw new NotImplementedException();

        public Task<IArtifactLocator> FindAsync( IActivityMonitor m, string type, string name, SVersion version )
        {
            throw new NotImplementedException();
        }

        public Task<bool> PushAsync( IActivityMonitor m, IEnumerable<IArtifactLocator> artifacts )
        {
            throw new NotImplementedException();
        }

        public string ResolveSecret( IActivityMonitor m, bool throwOnEmpty = false )
        {
            throw new NotImplementedException();
        }
    }
}
