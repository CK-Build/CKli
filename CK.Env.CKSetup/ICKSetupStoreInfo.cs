using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public interface ICKSetupStoreInfo : IArtifactRepositoryInfo
    {
        bool IsDefaultPublic { get; }

        string SecretKeyName { get; }

        string Name { get; }

        string Url { get; }
    }
}
