using CK.Core;
using NuGet.Configuration;

namespace CK.NuGetClient
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// </summary>
    class NuGetClientStandardFeed : NuGetRemoteFeedBase
    {
        public NuGetClientStandardFeed( NuGetClient c, NuGetStandardFeedInfo info )
            : base( c, new PackageSource( info.Url, info.Name ), info )
        {
        }

        /// <summary>
        /// Gets the <see cref="NuGetStandardFeedInfo"/> info.
        /// </summary>
        public new NuGetStandardFeedInfo Info => (NuGetStandardFeedInfo)base.Info;

        protected override string ResolvePushAPIKey( IActivityMonitor m ) => ResolveSecret( m );
    }
}
