using CK.Core;
using CK.Build;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    /// <summary>
    /// Internal implementation that may be made public once.
    /// </summary>
    class NPMAzureRepository : NPMRepositoryBase, INPMAzureRepository
    {
        internal NPMAzureRepository(
            NPMClient c,
            PackageQualityFilter qualityFilter,
            string organization,
            string feedName,
            string scope,
            string projectName )
            : base( c,
                    qualityFilter,
                    $"Azure:{scope}->{organization}-{feedName}",
                    projectName != null ?
                      $"https://pkgs.dev.azure.com/{organization}/{projectName}/_packaging/{feedName}/npm/registry/"
                    : $"https://pkgs.dev.azure.com/{organization}/_packaging/{feedName}/npm/registry/" )
        {
            Organization = organization;
            FeedName = feedName;
            Scope = scope;
            ProjectName = projectName;
        }

        /// <summary>
        /// Gets the organization name.
        /// </summary>
        public string Organization { get; }

        /// <summary>
        /// Gets the name of the feed inside the <see cref="Organization"/>.
        /// </summary>
        public string FeedName { get; }

        /// <summary>
        /// Gets the "@Scope" string: it MUST start with a @ and be a non empty scope name.
        /// </summary>
        public string Scope { get; }

        /// <summary>
        /// Gets the project name of this Repository. Can be null.
        /// </summary>
        public string ProjectName { get; }

        /// <summary>
        /// The secret key name is:
        /// "AZURE_FEED_" + Organization.ToUpperInvariant().Replace( '-', '_' ).Replace( ' ', '_' ) + "_PAT".
        /// </summary>
        public override string SecretKeyName => AzureDevOpsAPIHelper.GetSecretKeyName( Organization );

        protected override Registry CreateRegistry( IActivityMonitor m, bool throwOnError )
        {
            string pat = ResolveSecret( m, throwOnError );
            if( string.IsNullOrWhiteSpace( pat ) ) return null;
            return new Registry( Client.HttpClient, "CKli", pat, new Uri( Url ) );
        }

        /// <summary>
        /// Gets the url api.
        /// </summary>
        /// <param name="point">The API point (after the /NPM/).</param>
        /// <param name="version">The API version.</param>
        /// <returns>The url.</returns>
        protected string GetAzureDevOpsUrlAPI( string point = "packagesBatch", string version = "api-version=5.0-preview.1" )
        {
            return AzureDevOpsAPIHelper.GetUrl( ProjectName, Organization, FeedName, true, point, version );
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
            return AzureDevOpsAPIHelper.PromotePackagesAync( m, Client.HttpClient, ProjectName, Organization, FeedName, personalAccessToken, packages, true );
        }

    }
}
