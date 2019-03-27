using System;

namespace CK.Env
{
    public interface ICKSetupStoreInfo : IArtifactRepositoryInfo
    {
        bool IsDefaultPublic { get; }

        string Name { get; }

        Uri Url { get; }
    }
}
