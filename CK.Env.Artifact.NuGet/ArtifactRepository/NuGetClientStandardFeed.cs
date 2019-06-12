using CK.Core;
using CSemVer;
using NuGet.Configuration;

namespace CK.Env.NuGet
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// </summary>
    class NuGetClientStandardFeed : NuGetRemoteFeedBase
    {
        internal NuGetClientStandardFeed(
            NuGetClient c,
            string url,
            string name,
            PackageQualityFilter qualityFilter,
            string secretKeyName )
            : base( c, new PackageSource( url, name ), qualityFilter )
        {
            SecretKeyName = secretKeyName;
        }

        public override string SecretKeyName { get; }

        protected override string ResolvePushAPIKey( IActivityMonitor m ) => ResolveSecret( m );
    }
}
