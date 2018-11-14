using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        /// <summary>
        /// Gets the <see cref="NuGetStandardFeedInfo.SecretKeyName"/>.
        /// </summary>
        public override string SecretKeyName => Info.SecretKeyName;

        protected override string ResolvePushAPIKey( IActivityMonitor m ) => ResolveSecret( m );
    }
}
