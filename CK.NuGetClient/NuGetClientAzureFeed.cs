using CK.Core;
using CSemVer;
using NuGet.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CK.NuGetClient
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
        protected string GetAzureDevOpsUrlAPI( string point = "packagesBatch", string version = "api-version=5.0" )
        {
            return $"https://pkgs.dev.azure.com/{Info.Organization}/_apis/packaging/feeds/{Info.FeedName}/nuget/{point}?{version}";
        }

        /// <summary>
        /// Implements Package promotion in @CI, @Preview, @Latest and @Stable views.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="skipped">The set of packages skipped because they already exist in the feed.</param>
        /// <param name="pushed">The set of packages pushed.</param>
        /// <returns>The awaitable.</returns>
        protected override async Task OnAllPackagesPushed( NuGetLoggerAdapter logger, IReadOnlyList<LocalNuGetPackageFile> skipped, IReadOnlyList<LocalNuGetPackageFile> pushed )
        {
            string personalAccessToken = ResolveSecret( logger.Monitor );
            var basicAuth = Convert.ToBase64String( Encoding.ASCII.GetBytes( ":" + personalAccessToken ) );
            foreach( var p in skipped.Concat( pushed ) )
            {
                foreach( var view in p.Version.PackageQuality.GetLabels() )
                {
                    using( HttpRequestMessage req = new HttpRequestMessage( HttpMethod.Post, GetAzureDevOpsUrlAPI() ) )
                    {
                        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Basic", basicAuth );
                        var body = GetPromotionJSONBody( p.PackageId, p.Version.ToNuGetPackageString(), view.ToString() );
                        req.Content = new StringContent( body, Encoding.UTF8, "application/json" );
                        using( var m = await Client.HttpClient.SendAsync( req ) )
                        {
                            if( m.IsSuccessStatusCode )
                            {
                                logger.Monitor.Info( $"Package '{p}' promoted to view '@{view}'." );
                            }
                            else
                            {
                                logger.Monitor.Error( $"Package '{p}' promotion to view '@{view}' failed." );
                                m.EnsureSuccessStatusCode();
                            }
                        }
                    }
                }
            }
        }

        string GetPromotionJSONBody( string packageName, string packageVersion, string viewId, bool npm = false )
        {
            var bodyFormat = @"{
 ""data"": {
    ""viewId"": ""{viewId}""
  },
  ""operation"": 0,
  ""packages"": [{
    ""id"": ""{packageName}"",
    ""version"": ""{packageVersion}"",
    ""protocolType"": ""{NuGetOrNpm}""
  }]
}";
            return bodyFormat.Replace( "{NuGetOrNpm}", npm ? "Npm" : "NuGet" )
                             .Replace( "{viewId}", viewId )
                             .Replace( "{packageName}", packageName )
                             .Replace( "{packageVersion}", packageVersion );
        }

    }
}
