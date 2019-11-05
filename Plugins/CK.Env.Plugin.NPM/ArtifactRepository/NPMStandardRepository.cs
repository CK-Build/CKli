using CK.Core;
using CSemVer;
using System;

namespace CK.Env.NPM
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// </summary>
    class NPMStandardRepository : NPMRepositoryBase, INPMStandardRepository
    {
        public NPMStandardRepository(
            NPMClient c,
            PackageQualityFilter qualityFilter,
            string name,
            string url,
            string secretKeyName,
            bool usePassword )
            : base( c, qualityFilter, name, url )
        {
            SecretKeyName = secretKeyName;
            UsePassword = usePassword;
        }

        public override string SecretKeyName { get; }

        /// <summary>
        /// Gets whether the NPM registry uses password instead of Personal Access Token.
        /// </summary>
        public bool UsePassword { get; }

        protected override Registry CreateRegistry( IActivityMonitor m, bool throwOnError )
        {
            var u = new Uri( Url );
            string secret = ResolveSecret( m, throwOnError );
            if( secret == null ) return null;
            return UsePassword
                    ? new Registry( Client.HttpClient, "CKli", secret, u )
                    : new Registry( Client.HttpClient, secret, u );
        }
    }
}
