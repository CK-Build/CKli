using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public interface ICKSetupStoreInfo : IArtifactRepositoryInfo
    {
        bool IsDefaultPublic { get; }

        string Name { get; }

        Uri Url { get; }
    }
}
