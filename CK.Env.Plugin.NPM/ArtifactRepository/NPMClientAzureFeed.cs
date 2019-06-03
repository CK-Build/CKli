using CK.Core;
using CSemVer;
using Npm.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// </summary>
    class NPMClientAzureFeed : NPMRemoteFeedBase, INPMFeed
    {
        internal NPMClientAzureFeed( NPMClient c, NPMAzureFeedInfo info, string pat )
            : base( c, info, new Registry(c.HttpClient, "", pat, new System.Uri( info.Url)) )
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
        protected string GetAzureDevOpsUrlAPI( string point = "packagesBatch", string version = "api-version=5.0-preview.1" )
        {
            return AzureDevOpsAPIHelper.GetUrl( Info.Organization, Info.FeedName, true, point, version );
        }

        /// <summary>
        /// Implements Package promotion in @CI, @Exploratory, @Preview, @Latest and @Stable views.
        /// </summary>
        /// <param name="m">The logger.</param>
        /// <param name="skipped">The set of packages skipped because they already exist in the feed.</param>
        /// <param name="pushed">The set of packages pushed.</param>
        /// <returns>The awaitable.</returns>
        protected override Task OnAllPackagesPushed( IActivityMonitor m, IReadOnlyList<LocalNPMPackageFile> skipped, IReadOnlyList<LocalNPMPackageFile> pushed )
        {
            string personalAccessToken = ResolveSecret( m );
            var packages = skipped.Concat( pushed ).Select( i => i.Instance );
            return AzureDevOpsAPIHelper.PromotePackagesAync( m, Client.HttpClient, Info.Organization, Info.FeedName, personalAccessToken, packages, true );
        }

    }
}
