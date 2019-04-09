using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CK.NPMClient
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// </summary>
    class NPMClientAzureFeed : NPMRemoteFeedBase, INPMFeed
    {
        internal NPMClientAzureFeed( NPMClient c, NPMAzureFeedInfo info )
            : base( c, info )
        {
        }

        /// <summary>
        /// Gets the <see cref="NPMAzureFeedInfo"/> info.
        /// </summary>
        public new NPMAzureFeedInfo Info => (NPMAzureFeedInfo)base.Info;

        /// <summary>
        /// Always "VSTS" or null if <see cref="ResolveSecret"/> returns null.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The API key or null.</returns>
        protected override string ResolvePushAPIKey( IActivityMonitor m ) => ResolveSecret( m ) != null ? "VSTS" : null;

        /// <summary>
        /// Gets the url api.
        /// </summary>
        /// <param name="point">The API point (after the /NPM/).</param>
        /// <param name="version">The API version.</param>
        /// <returns>The url.</returns>
        protected string GetAzureDevOpsUrlAPI( string point = "packagesBatch", string version = "api-version=5.0" )
        {
            return $"https://pkgs.dev.azure.com/{Info.Organization}/_apis/packaging/feeds/{Info.FeedName}/NPM/{point}?{version}";
        }

    }
}
