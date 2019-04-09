using CK.Core;

namespace CK.NPMClient
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// </summary>
    class NPMClientStandardFeed : NPMRemoteFeedBase
    {
        public NPMClientStandardFeed( NPMClient c, NPMStandardFeedInfo info )
            : base( c, info )
        {
        }

        /// <summary>
        /// Gets the <see cref="NPMStandardFeedInfo"/> info.
        /// </summary>
        public new NPMStandardFeedInfo Info => (NPMStandardFeedInfo)base.Info;

        protected override string ResolvePushAPIKey( IActivityMonitor m ) => ResolveSecret( m );
    }
}
