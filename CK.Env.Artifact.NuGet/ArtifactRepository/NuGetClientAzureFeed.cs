using CK.Core;
using NuGet.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.Env.NuGet
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// </summary>
    class NuGetClientAzureFeed : NuGetRemoteFeedBase, INuGetFeed
    {
        internal NuGetClientAzureFeed( NuGetClient c, NuGetAzureFeedInfo info )
            : base( c, new PackageSource( info.Url, info.Name ), info )
        {
        }

        /// <summary>
        /// Gets the <see cref="NuGetAzureFeedInfo"/> info.
        /// </summary>
        public new NuGetAzureFeedInfo Info => (NuGetAzureFeedInfo)base.Info;

        /// <summary>
        /// Always "VSTS" or null if <see cref="ResolveSecret"/> returns null.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The API key or null.</returns>
        protected override string ResolvePushAPIKey( IActivityMonitor m ) => ResolveSecret( m ) != null ? "VSTS" : null;

        protected override void OnSecretResolved( IActivityMonitor m, string secret )
        {
            NuGetClient.EnsureVSSFeedEndPointCredentials( m, Info.Url, secret );
        }

        /// <summary>
        /// Gets the url api.
        /// </summary>
        /// <param name="point">The API point (after the /nuget/).</param>
        /// <param name="version">The API version.</param>
        /// <returns>The url.</returns>
        protected string GetAzureDevOpsUrlAPI( string point = "packagesBatch", string version = "api-version=5.0-preview.1" )
        {
            return AzureDevOpsAPIHelper.GetUrl( Info.Organization, Info.FeedName, false, point, version );
        }

        /// <summary>
        /// Implements Package promotion in @CI, @Exploratory, @Preview, @Latest and @Stable views.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="skipped">The set of packages skipped because they already exist in the feed.</param>
        /// <param name="pushed">The set of packages pushed.</param>
        /// <returns>The awaitable.</returns>
        protected override Task OnAllPackagesPushed( NuGetLoggerAdapter logger, IReadOnlyList<LocalNuGetPackageFile> skipped, IReadOnlyList<LocalNuGetPackageFile> pushed )
        {
            string personalAccessToken = ResolveSecret( logger.Monitor );
            var packages = skipped.Concat( pushed ).Select( i => i.Instance );
            return AzureDevOpsAPIHelper.PromotePackagesAync( logger.Monitor, Client.HttpClient, Info.Organization, Info.FeedName, personalAccessToken, packages, false );
        }

    }
}
