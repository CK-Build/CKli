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
    /// </summary>
    class NuGetAzureRepository : NuGetRepositoryBase, INuGetAzureRepository
    {
        internal NuGetAzureRepository( NuGetClient c,
                                       string name,
                                       PackageQualityFilter qualityFilter,
                                       string organization,
                                       string feedName,
                                       string? label,
                                       string? projectName )
            : base( c,
                    new PackageSource( projectName != null
                                            ? $"https://pkgs.dev.azure.com/{organization}/{projectName}/_packaging/{feedName}{label}/nuget/v3/index.json"
                                            : $"https://pkgs.dev.azure.com/{organization}/_packaging/{feedName}{label}/nuget/v3/index.json",
                                       name ),
                    qualityFilter )
        {
            Organization = organization;
            FeedName = feedName;
            Label = label;
            PublicProjectName = projectName;
        }

        /// <summary>
        /// The secret key name is:
        /// "AZURE_FEED_" + Organization.ToUpperInvariant().Replace( '-', '_' ).Replace( ' ', '_' ) + "_PAT".
        /// </summary>
        public override string? SecretKeyName => AzureDevOpsAPIHelper.GetSecretKeyName( Organization );

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
        public string? Label { get; }

        /// <summary>
        /// Gets the project name of this Repository. Can be null.
        /// </summary>
        public string? PublicProjectName { get; }

        /// <summary>
        /// Always "VSTS" or null if <see cref="ResolveSecret"/> returns null.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The API key or null.</returns>
        protected override string? ResolvePushAPIKey( IActivityMonitor monitor ) => ResolveSecret( monitor ) != null ? "VSTS" : null;

        protected override void OnSecretResolved( IActivityMonitor monitor, string secret )
        {
            NuGetClient.EnsureVSSFeedEndPointCredentials( monitor, Url, secret );
        }

        /// <summary>
        /// Gets the url api.
        /// </summary>
        /// <param name="point">The API point (after the /nuget/).</param>
        /// <param name="version">The API version.</param>
        /// <returns>The url.</returns>
        protected string GetAzureDevOpsUrlAPI( string point = "packagesBatch", string version = "api-version=5.0-preview.1" )
        {
            return AzureDevOpsAPIHelper.GetUrl( PublicProjectName, Organization, FeedName, false, point, version );
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
            string? personalAccessToken = ResolveSecret( logger.Monitor );
            var packages = skipped.Concat( pushed ).Select( i => i.Instance );
            return AzureDevOpsAPIHelper.PromotePackagesAync( logger.Monitor, Client.HttpClient, PublicProjectName, Organization, FeedName, personalAccessToken, packages, false );
        }

    }
}
