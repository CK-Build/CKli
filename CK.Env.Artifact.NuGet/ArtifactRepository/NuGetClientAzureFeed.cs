using CK.Core;
using CSemVer;
using NuGet.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.Env.NuGet
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// The name of this feed is <see cref="Organization"/>-<see cref="FeedName"/>[-<see cref="Label"/>(without the '@' label prefix)].
    /// </summary>
    class NuGetClientAzureFeed : NuGetRemoteFeedBase
    {
        internal NuGetClientAzureFeed(
            NuGetClient c,
            string url,
            string name,
            PackageQualityFilter qualityFilter,
            string organization,
            string feedName,
            string label )
            : base( c, new PackageSource( url, name ), qualityFilter )
        {
            Organization = organization;
            FeedName = feedName;
            Label = label;
        }

        /// <summary>
        /// The secret key name is:
        /// "AZURE_FEED_" + Organization.ToUpperInvariant().Replace( '-', '_' ).Replace( ' ', '_' ) + "_PAT".
        /// </summary>
        public override string SecretKeyName => AzureDevOpsAPIHelper.GetSecretKeyName( Organization );

        /// <summary>
        /// Gets the organization name.
        /// </summary>
        public string Organization { get; }

        /// <summary>
        /// Gets the name of the feed inside the <see cref="Organization"/>.
        /// </summary>
        public string FeedName { get; }

        /// <summary>
        /// Gets the "@Label" string or null.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Always "VSTS" or null if <see cref="ResolveSecret"/> returns null.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The API key or null.</returns>
        protected override string ResolvePushAPIKey( IActivityMonitor m ) => ResolveSecret( m ) != null ? "VSTS" : null;

        protected override void OnSecretResolved( IActivityMonitor m, string secret )
        {
            NuGetClient.EnsureVSSFeedEndPointCredentials( m, PackageSource.Source, secret );
        }

        /// <summary>
        /// Gets the url api.
        /// </summary>
        /// <param name="point">The API point (after the /nuget/).</param>
        /// <param name="version">The API version.</param>
        /// <returns>The url.</returns>
        protected string GetAzureDevOpsUrlAPI( string point = "packagesBatch", string version = "api-version=5.0-preview.1" )
        {
            return AzureDevOpsAPIHelper.GetUrl( Organization, FeedName, false, point, version );
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
            return AzureDevOpsAPIHelper.PromotePackagesAync( logger.Monitor, Client.HttpClient, Organization, FeedName, personalAccessToken, packages, false );
        }

    }
}
