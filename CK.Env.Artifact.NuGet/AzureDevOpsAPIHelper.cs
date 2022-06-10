using CK.Core;

using CK.Build;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace CK.Env
{
    /// <summary>
    /// Internal class shared with CK.Env.Plugin.NPM.
    /// </summary>
    internal static class AzureDevOpsAPIHelper
    {

        /// <summary>
        /// The secret key name is:
        /// "AZURE_FEED_" + Organization.ToUpperInvariant().Replace( '-', '_' ).Replace( ' ', '_' ) + "_PAT".
        /// </summary>
        public static string GetSecretKeyName( string organization )
            => "AZURE_FEED_"
                + organization
                        .ToUpperInvariant()
                        .Replace( '-', '_' )
                        .Replace( ' ', '_' )
                + "_PAT";


        /// <summary>
        /// Gets the url api.
        /// </summary>
        /// <param name="publicProjectName">Project name for Open Source (public) Azure Feed. Must be null for private feed.</param>
        /// <param name="organization">The organization name.</param>
        /// <param name="feedName">The name of the feed.</param>
        /// <param name="isNPM">True for NPM, false for NuGet.</param>
        /// <param name="point">The API point (after the /npm/ or /nuget/). Can contain multiple/segments/if/needed.</param>
        /// <param name="version">The API version.</param>
        /// <returns>The url.</returns>
        public static string GetUrl( string? publicProjectName, string organization, string feedName, bool isNPM, string point = "packagesBatch", string version = "api-version=5.0-preview.1" )
        {
            if( publicProjectName == null )
            {
                return $"https://pkgs.dev.azure.com/{organization}/_apis/packaging/feeds/{feedName}/{(isNPM ? "npm" : "nuget")}/{point}?{version}";
            }
            return $"https://pkgs.dev.azure.com/{organization}/{publicProjectName}/_apis/packaging/feeds/{feedName}/{(isNPM ? "npm" : "nuget")}/{point}?{version}";
        }


        /// <summary>
        /// Implements Package promotion in @CI, @Exploratory, @Preview, @Latest and @Stable views.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="httpClient">The http client.</param>
        /// <param name="publicProjectName">Public project name or null.</param>
        /// <param name="organization">The organization name.</param>
        /// <param name="feedName">The name of the feed.</param>
        /// <param name="isNPM">True for NPM, false for NuGet.</param>
        /// <param name="personalAccessToken">The personal access token.</param>
        /// <param name="packages">The set of packages to promote.</param>
        /// <returns></returns>
        public static async Task PromotePackagesAync( IActivityMonitor monitor,
                                                      HttpClient httpClient,
                                                      string? publicProjectName,
                                                      string organization,
                                                      string feedName,
                                                      string personalAccessToken,
                                                      IEnumerable<ArtifactInstance> packages,
                                                      bool isNPM )
        {
            string apiUrl = AzureDevOpsAPIHelper.GetUrl( publicProjectName, organization, feedName, isNPM, "packagesBatch", "api-version=5.0-preview.1" );
            var basicAuth = Convert.ToBase64String( Encoding.ASCII.GetBytes( ":" + personalAccessToken ) );
            var byLabels = packages
                                .SelectMany( p => p.Version.PackageQuality.GetAllQualities().Select( label => (label, p) ) )
                                .GroupBy( labeledPackage => labeledPackage.label, labeledPackage => labeledPackage.p );
            foreach( var set in byLabels )
            {
                var viewName = set.Key.ToString();
                using( monitor.OpenInfo( $"Promoting into view '@{viewName}'." ) )
                {
                    //  Batches must not exceed 100 items.
                    var batches = set.Select( ( f, i ) => (i / 100, f) ).GroupBy( gF => gF.Item1, g => g.f );
                    foreach( var b in batches )
                    {
                        using( HttpRequestMessage req = new HttpRequestMessage( HttpMethod.Post, apiUrl ) )
                        {
                            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Basic", basicAuth );
                            req.Content = new ByteArrayContent( GetPromotionJSONBody( b, viewName, isNPM ) );
                            req.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse( "application/json" );
                            req.Content.Headers.ContentEncoding.Add( Encoding.UTF8.WebName );
                            using( var msg = await httpClient.SendAsync( req ) )
                            {
                                if( !msg.IsSuccessStatusCode )
                                {
                                    monitor.Error( $"Failed." );
                                    msg.EnsureSuccessStatusCode();
                                }
                            }
                        }
                    }
                }
            }
        }

        static byte[] GetPromotionJSONBody( IEnumerable<ArtifactInstance> packages, string viewName, bool npm )
        {
            var a = new ByteArrayWriter();
            using( var w = new Utf8JsonWriter( a ) )
            {
                w.WriteStartObject();
                w.WritePropertyName( "data" );
                w.WriteStartObject();
                w.WriteString( "viewId", viewName );
                w.WriteEndObject();
                w.WriteNumber( "operation", 0 );
                w.WritePropertyName( "packages" );
                w.WriteStartArray();
                foreach( var p in packages )
                {
                    w.WriteStartObject();
                    w.WriteString( "id", p.Artifact.Name );
                    w.WriteString( "version", p.Version.ToNormalizedString() );
                    w.WriteString( "protocolType", npm ? "Npm" : "NuGet" );
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
                w.Flush();
                return a.WrittenSpan.ToArray();
            }
        }

    }
}
