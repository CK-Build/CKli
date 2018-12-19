using System;
using System.Xml.Linq;

namespace CK.Env
{
    class CKSetupStoreRepositoryInfo : ICKSetupStoreInfo
    {
        public CKSetupStoreRepositoryInfo( string url, string name )
        {
            Name = name;
            Url = url;
        }

        public bool IsDefaultPublic => false;

        public string SecretKeyName => $"CKSETUPREMOTESTORE_{Name.ToUpperInvariant()}_PUSH_API_KEY";

        public string Name { get; }

        public string Url { get; }

        string IArtifactRepositoryInfo.UniqueArtifactRepositoryName => $"CKSetup:{Name}";
    }
}
