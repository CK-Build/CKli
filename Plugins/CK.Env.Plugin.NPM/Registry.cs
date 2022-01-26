using CK.Core;

using CSemVer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    public class Registry
    {
        static readonly string _userAgent = "CKli";

        public static readonly Uri NPMJSOrgUri = new Uri( "https://registry.npmjs.org/" );

        readonly HttpClient _httpClient;
        readonly AuthenticationHeaderValue _authHeader;
        readonly string _session = GenerateSessionId();

        readonly string _password;
        readonly string _username;

        /// <summary>
        /// NPM ask a one time password, so here you have one.
        /// </summary>
        /// <returns></returns>
        static string GenerateSessionId()
        {
            byte[] bytes = new byte[8];
            using( var r = RandomNumberGenerator.Create() ) r.GetBytes( bytes );
            return BitConverter.ToString( bytes ).Replace( "-", "" ).ToLower();
        }

        /// <summary>
        /// Initializes a new Registry without any authentication.
        /// </summary>
        /// <param name="httpClient">The http client that will be used.</param>
        /// <param name="uri">The uri of the npm repository.</param>
        public Registry( HttpClient httpClient, Uri uri )
        {
            RegistryUri = uri;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Initializes a new Registry that uses a Authentication bearer token.
        /// </summary>
        /// <param name="httpClient">The http client to use.</param>
        /// <param name="token">The token to use.</param>
        /// <param name="uri">Defaults to https://registry.npmjs.org/ </param>
        public Registry( HttpClient httpClient, string token, Uri? uri = null )
            : this( httpClient, uri ?? NPMJSOrgUri )
        {
            _authHeader = new AuthenticationHeaderValue( "Bearer", token );
        }

        /// <summary>
        /// Initializes a new Registry that uses basic authentication.
        /// </summary>
        /// <param name="httpClient">The http client to use.</param>
        /// <param name="username">The user name.</param>
        /// <param name="password">The password.</param>
        /// <param name="uri">Defaults to https://registry.npmjs.org/ </param>
        public Registry( HttpClient httpClient, string username, string password, Uri uri = null )
            : this( httpClient, uri ?? NPMJSOrgUri )
        {
            _username = username;
            _password = password;
            string basic = Convert.ToBase64String( Encoding.ASCII.GetBytes( $"{username}:{password}" ) );
            _authHeader = new AuthenticationHeaderValue( "Basic", basic );
        }

        /// <summary>
        /// Uri of the registry
        /// </summary>
        public Uri RegistryUri { get; }

        /// <summary>
        /// Gets or Sets whether we are running in CI or not.
        /// The fields is automatically set based on the environment variables with the same behavior as npm.
        /// </summary>
        public bool NpmInCi { get; set; } = Environment.GetEnvironmentVariable( "CI" ) == "true" ||
                                            Environment.GetEnvironmentVariable( "TDDIUM" ) != null ||
                                            Environment.GetEnvironmentVariable( "JENKINS_URL" ) != null ||
                                            Environment.GetEnvironmentVariable( "bamboo.buildKey" ) != null ||
                                            Environment.GetEnvironmentVariable( "GO_PIPELINE_NAME" ) != null;

        /// <summary>
        /// Gets the dist tags for the package.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="packageName">The package name.</param>
        /// <returns>The raw JSON object.</returns>
        public async Task<string?> GetDistTags( IActivityMonitor m, string packageName )
        {
            using( HttpRequestMessage req = NpmRequestMessage( m, $"/-/package/{packageName}/dist-tags", HttpMethod.Get ) )
            using( HttpResponseMessage response = await _httpClient.SendAsync( req ) )
            {
                return await HandleResponse( m, response )
                        ? await response.Content.ReadAsStringAsync()
                        : null;
            }
        }

        public async Task<bool> AddDistTag( IActivityMonitor m, string packageName, SVersion version, string tagName )
        {
            if( RegistryUri.IsFile )
            {
                m.Warn( "Dist tags are not supported on a filesystem registry." );
                return true;
            }
            if( String.IsNullOrWhiteSpace( tagName ) ) throw new ArgumentNullException( nameof( tagName ) );
            tagName = tagName.ToLowerInvariant();
            packageName = WebUtility.UrlEncode( packageName );
            using( HttpRequestMessage req = NpmRequestMessage( m, $"/-/package/{packageName}/dist-tags/{tagName}", HttpMethod.Put ) )
            {
                req.Content = new StringContent( "\"" + version.ToNormalizedString() + "\"" );
                req.Content.Headers.ContentType.MediaType = "application/json";
                req.Content.Headers.ContentType.CharSet = "";
                using( HttpResponseMessage response = await _httpClient.SendAsync( req ) )
                {
                    return await HandleResponse( m, response );
                }
            }
        }

        /// <summary>
        /// Publish a package to the repository
        /// </summary>
        /// <param name="m"></param>
        /// <param name="packageJson">The package.json of the package to push</param>
        /// <param name="tarball">This stream must be Seek-able. <see cref="Stream"/> of the tarball of the package to push.</param>
        /// <param name="distTag"></param>
        /// <returns></returns>
        public bool Publish( IActivityMonitor m, NormalizedPath tarballPath, bool isPublic, string? scope = null, string? distTag = null )
        {
            if( RegistryUri.IsFile )
            {
                var path = Path.Combine( RegistryUri.AbsolutePath, tarballPath.LastPart );
                if( File.Exists( path ) ) return true;
                try
                {
                    File.Copy( tarballPath, path );
                    return true;
                }
                catch( Exception e )
                {
                    m.Error( e );
                }
                return false;
            }
            string tempDirectory = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
            using( m.OpenInfo( "Using 'npm publish'." ) )
            {
                try
                {
                    Directory.CreateDirectory( tempDirectory );
                    m.Debug( $"Creating temp directory {tempDirectory}." );
                    using( m.OpenInfo( "Creating .npmrc with content:" ) )
                    using( StreamWriter w = File.CreateText( Path.Combine( tempDirectory, ".npmrc" ) ) )
                    {
                        string uriString = RegistryUri.ToString();
                        w.WriteLine( $"registry={uriString}" );
                        m.Debug( $"registry={uriString}" );
                        string uriConfig = uriString.Remove( 0, uriString.IndexOf( '/' ) );

                        if( _authHeader == null )
                        {
                            m.Error( "Missing credentials to configure .npmrc file." );
                            return false;
                        }
                        if( _authHeader.Scheme == "Basic" )
                        {
                            w.WriteLine( $"{uriConfig}:always-auth=true" );
                            m.Debug( $"{uriConfig}:always-auth=true" );

                            w.WriteLine( $"{uriConfig}:_password={Convert.ToBase64String( Encoding.UTF8.GetBytes( _password ) )}" );
                            m.Debug( $"{uriConfig}:_password=[REDACTED]" );

                            w.WriteLine( $"{uriConfig}:username={_username}" );
                            m.Debug( $"{uriConfig}:username={_username}" );
                        }
                        else if( _authHeader.Scheme == "Bearer" )
                        {
                            w.WriteLine( $"{uriConfig}:always-auth=true" );
                            m.Debug( $"{uriConfig}:always-auth=true" );
                            w.WriteLine( $"{uriConfig}:_authToken={_authHeader.Parameter}" );
                            m.Debug( $"{uriConfig}:_authToken=[REDACTED]" );
                        }

                        if( !string.IsNullOrWhiteSpace( scope ) )
                        {
                            w.WriteLine( scope + $":registry={RegistryUri}" );
                            m.Debug( scope + $":registry={RegistryUri}" );
                        }
                    }
                    string tarPath = Path.GetFullPath( tarballPath );
                    string distTagArg = distTag != null ? $"--tag {distTag.ToLowerInvariant()}" : "";
                    string access = isPublic ? "public" : "private";
                    return ProcessRunner.Run( m, tempDirectory, "cmd.exe", $"/C npm publish \"{tarPath}\" --access {access} {distTagArg}", 10 * 60 * 1000, LogLevel.Info );
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return false;
                }
                finally
                {
                    try
                    {
                        Directory.Delete( tempDirectory, true );
                    }
                    catch( Exception ex )
                    {
                        m.Warn( $"While destroying temporary folder: {tempDirectory}", ex );
                    }
                }
            }
        }


        public async Task<bool> ExistAsync( IActivityMonitor m, string packageName, SVersion version )
        {
            if( RegistryUri.IsFile )
            {
                return File.Exists( Path.Combine( RegistryUri.AbsolutePath, +'-' + version.ToNormalizedString() + ".tgz" ) );
            }
            var (doc,versions) = await CreateViewDocumentAndVersionsElementAsync( m, packageName, true );
            if( doc == null ) return false;
            bool exists = versions.TryGetProperty( version.ToNormalizedString(), out _ );
            doc.Dispose();
            return exists;
        }

        /// <summary>
        /// Create a request with the same header than npm would send.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        HttpRequestMessage NpmRequestMessage( IActivityMonitor m, string endpoint, HttpMethod method ) => NpmRequestMessage( m, new Uri( RegistryUri + endpoint ), method );

        HttpRequestMessage NpmRequestMessage( IActivityMonitor m, Uri fullUri, HttpMethod method )
        {
            var req = new HttpRequestMessage
            {
                RequestUri = fullUri,
                Method = method
            };
            m.Info( $"Request URI: {req.Method}:'{req.RequestUri}'." );
            AddNpmHeaders( m, req.Headers );
            return req;
        }

        void AddNpmHeaders( IActivityMonitor m, HttpRequestHeaders headers )
        {
            if( NpmInCi )
            {
                headers.Add( "npm-in-ci", NpmInCi.ToString().ToLower() ); //json type are lowercase
                m.Info( "Detected that we are running in CI. Sending to the registry an header indicating it." );
            }
            headers.Add( "npm-session", _session );
            headers.Add( "user-agent", _userAgent );
            if( _authHeader != null )
            {
                headers.Authorization = _authHeader;
            }
        }

        /// <summary>
        /// Creates a JsonDocument with the package infos that MUST be disposed once done.
        /// The json format is available here: https://github.com/npm/registry/blob/master/docs/responses/package-metadata.md
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="endpoint">The uri to call.</param>
        /// <param name="abbreviatedResponse">Ask the server to abbreviate the returned info.</param>
        /// <returns>The JsonDocument or null on error.</returns>
        public async Task<JsonDocument?> CreateViewDocumentAsync( IActivityMonitor monitor, string endpoint, bool abbreviatedResponse )
        {
            using( HttpRequestMessage req = NpmRequestMessage( monitor, endpoint, HttpMethod.Get ) )
            {
                if( abbreviatedResponse )
                {
                    req.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/vnd.npm.install-v1+json", 1.0 ) );
                    req.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json", 0.8 ) );
                    req.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "*/*", 0.8 ) );
                }
                try
                {
                    using( var response = await _httpClient.SendAsync( req ) )
                    {
                        if( !await HandleResponse( monitor, response ) ) return null;
                        return await JsonDocument.ParseAsync( await response.Content.ReadAsStreamAsync() );
                    }
                }
                catch( Exception ex )
                {
                    monitor.Error( ex );
                    return null;
                }
            }
        }

        /// <summary>
        /// Creates a JsonDocument with the package infos that MUST be disposed once done.
        /// Before returning a non null document, the "versions" element is found.
        /// The json format is available here: https://github.com/npm/registry/blob/master/docs/responses/package-metadata.md
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="endpoint">The uri to call.</param>
        /// <param name="abbreviatedResponse">Ask the server to abbreviate the returned info.</param>
        /// <returns>The JsonDocument and versions element or null on error.</returns>
        public async Task<(JsonDocument?,JsonElement)> CreateViewDocumentAndVersionsElementAsync( IActivityMonitor monitor,
                                                                                                  string endpoint,
                                                                                                  bool abbreviatedResponse )
        {
            var d = await CreateViewDocumentAsync( monitor, endpoint, abbreviatedResponse );
            if( d != null )
            {
                var versions = d.RootElement.GetProperty( "versions" );
                if( versions.ValueKind == JsonValueKind.Object )
                {
                    return (d, versions);
                }
                monitor.Error( $"Unable to find a \"versions\" object in response body." );
                d.Dispose();
            }
            return (null, default);
        }

        async Task<bool> HandleResponse( IActivityMonitor m, HttpResponseMessage res )
        {
            HttpResponseHeaders headers = res.Headers;
            if( headers.Contains( "npm-notice" ) && !headers.Contains( "x-local-cache" ) )
            {
                m.Info( $"npm-notice headers: {headers.GetValues( "npm-notice" ).Concatenate()}" );
            }
            DumpWarnings( m, headers );
            return await LogErrorsAsync( m, res );
        }


        /// <summary>
        /// Logs potential errors. Returns true if no error occurred, false on error.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="res"></param>
        /// <returns>True on success, false on error.</returns>
        async Task<bool> LogErrorsAsync( IActivityMonitor m, HttpResponseMessage res )
        {
            if( res.StatusCode == HttpStatusCode.Unauthorized )
            {
                using( m.OpenError( "Unauthorized Status Code" ) )
                {
                    if( res.Headers.Contains( "www-authenticate" ) )
                    {
                        List<string> auth = res.Headers.GetValues( "www-authenticate" ).ToList();
                        if( auth.Contains( "ipaddress" ) )
                        {
                            m.Error( "Login is not allowed from your IP address" );
                        }
                        else if( auth.Contains( "otp" ) )
                        {
                            m.Error( "OTP required for authentication" );
                        }
                        else
                        {
                            m.Error( "Unable to authenticate, need: " + string.Join( ", ", auth ) );
                        }
                    }
                    else
                    {
                        if( (await res.Content.ReadAsStringAsync()).Contains( "one-time pass" ) )
                        {
                            m.Error( "OTP required for authentication." );
                        }
                        else
                        {
                            m.Error( "Unknown error." );
                        }
                    }
                }
                return false;
            }
            if( !res.IsSuccessStatusCode )
            {
                using( m.OpenError( $"Response status code is not a success code: '{res.ReasonPhrase}'." ) )
                {
                    m.Trace( await res.Content.ReadAsStringAsync() );
                }
                return false;
            }
            else
            {
                m.Debug( "Response status code is a success status code." );
            }
            return true;
        }

        void DumpWarnings( IActivityMonitor m, HttpResponseHeaders responseHeaders )
        {
            if( responseHeaders.Contains( "warning" ) )
            {
                foreach( string warning in responseHeaders.GetValues( "warning" ) )
                {
                    m.Warn( $"NPM warning: {warning}" );
                    var match = Regex.Match( warning, @"/^\s*(\d{3})\s+(\S+)\s+""(.*)""\s+""([^""]+)""/" );
                    if( !int.TryParse( match.Groups[1].Value, out int code ) )
                    {
                        m.Error( "Incorrect warning header format." );
                        continue;
                    }
                    // Useless:
                    // string host = match.Groups[2].Value;
                    // DateTime date = JsonConvert.DeserializeObject<DateTime>( match.Groups[4].Value );
                    string message = match.Groups[3].Value;
                    if( code == 199 )
                    {
                        if( message.Contains( "ENOTFOUND" ) )
                        {
                            m.Warn( $"registry: Using stale data from {RegistryUri} because the host is inaccessible -- are you off-line?" );
                            m.Error( "Npm.Net is not using any caches, so you should not see the previous warning." );
                        }
                        else
                        {
                            m.Warn( $"Unexpected warning for {RegistryUri}: {message}" );
                        }
                    }
                    else if( code == 111 )
                    {
                        m.Warn( $"Using stale data from {RegistryUri} due to a request error during revalidation." );
                    }
                }
            }
        }

        public override string ToString() => RegistryUri.ToString();

    }
}
