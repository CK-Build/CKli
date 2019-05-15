using System;

namespace CK.Env.CKSetup
{
    class StoreInfo : ICKSetupStoreInfo
    {
        public StoreInfo( string url, string name )
        {
            Name = name;
            Url = new Uri( url );
        }

        public bool IsDefaultPublic => false;

        public string SecretKeyName => $"CKSETUPREMOTESTORE_{Name.ToUpperInvariant()}_PUSH_API_KEY";

        public string Name { get; }

        public Uri Url { get; }

        string IArtifactRepositoryInfo.UniqueArtifactRepositoryName => $"CKSetup:{Name}";
    }
}
