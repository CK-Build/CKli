using System;
using CKSetup;

namespace CK.Env.CKSetup
{
    class DefaultPublicStore : ICKSetupStoreInfo
    {
        private DefaultPublicStore()
        {
        }

        public static readonly ICKSetupStoreInfo Default = new DefaultPublicStore();

        /// <summary>
        /// Gets whether this is the default public store (<see cref="Facade.DefaultStoreUrl"/>).
        /// </summary>
        public bool IsDefaultPublic => true;

        public string UniqueArtifactRepositoryName => "CKSetup:Public";

        public string SecretKeyName => "CKSETUPREMOTESTORE_PUSH_API_KEY";

        public string Name => "Public";

        public PackageQualityFilter QualityFilter { get; }

        public Uri Url => Facade.DefaultStoreUrl;
    }
}