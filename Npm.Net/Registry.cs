using CK.Core;
using CK.Env;
using CK.Text;
using CSemVer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Npm.Net
{
    public class Registry
    {
        static readonly Uri _npmjs = new Uri( "https://registry.npmjs.org/" );
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
        /// No auth
        /// </summary>
        /// <param name="httpClient"></param>
        /// <param name="uri"></param>
        public Registry( HttpClient httpClient, Uri uri = null )
        {
            RegistryUri = uri ?? _npmjs;
            _httpClient = httpClient;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="httpClient"></param>
        /// <param name="token"></param>
        /// <param name="uri">if null the Uri is https://registry.npmjs.org/ </param>
        public Registry( HttpClient httpClient, string token, Uri uri = null )
            : this( httpClient, uri )
        {
            _authHeader = new AuthenticationHeaderValue( "Bearer", token );
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="httpClient"></param>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <param name="uri">if null the Uri is https://registry.npmjs.org/ </param>
        public Registry( HttpClient httpClient, string username, string password, Uri uri = null )
            : this( httpClient, uri )
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
        /// Gets or Sets wether we are running in CI or not.
        /// The fields is automatically setted based on the environement variables with the same bahavior of npm.
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
        public async Task<string> GetDistTags( IActivityMonitor m, string packageName )
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
            if( String.IsNullOrWhiteSpace( tagName ) ) throw new ArgumentNullException( nameof( tagName ) );
            tagName = tagName.ToLowerInvariant();
            packageName = WebUtility.UrlEncode( packageName );
            using( HttpRequestMessage req = NpmRequestMessage( m, $"/-/package/{packageName}/dist-tags/{tagName}", HttpMethod.Put ) )
            {
                req.Content = new StringContent( "\"" + version.ToNuGetPackageString() + "\"" );
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
        public async Task<bool> PublishAsync( IActivityMonitor m, Stream tarball, string distTag = null )
        {
            JObject packageJson;
            using( MemoryStream uglyBuffer = new MemoryStream() )
            {
                await tarball.CopyToAsync( uglyBuffer );
                uglyBuffer.Position = 0;
                using( GZipStream decompressed = new GZipStream( uglyBuffer, CompressionMode.Decompress, true ) )
                using( MemoryStream uglyDecompressedBuffer = new MemoryStream() )
                {
                    decompressed.CopyTo( uglyDecompressedBuffer );
                    uglyDecompressedBuffer.Position = 0;
                    packageJson = ExtractPackageJson( m, uglyDecompressedBuffer );
                }
                string packageName = packageJson["name"].ToString();
                try
                {
                    uglyBuffer.Position = 0;
                    using( HttpRequestMessage req = NpmRequestMessage( m, packageName, HttpMethod.Put ) )
                    using( MetadataStream metadataStream = MetadataStream.LegacyMetadataStream( m, RegistryUri, packageJson, uglyBuffer, distTag ) )
                    {
                        req.Content = metadataStream;
                        /**
                         * npm does more things than we does:
                         * if the first request return 409: conflict, npm fetch the versions availables in the registry.
                         * With these versions npm patch the metadata and resend a packet.
                         * We think it's probably for legacy reason, the simple request works on Azure Devops and Verdaccio
                         * https://github.com/npm/npm-registry-client/commit/e9fbeb8b67f249394f735c74ef11fe4720d46ca0
                         * TL;DR: The legacy npm publish is not implemented.
                         **/
                        using( var response = await _httpClient.SendAsync( req ) )
                        {
                            return await HandleResponse( m, response )
                                    || PublishViaNPMProcess( m, uglyBuffer, packageName, distTag );
                        }
                    }
                }
                catch( Exception e )
                {
                    m.Error( "While publishing via the API.", e );
                    return PublishViaNPMProcess( m, uglyBuffer, packageName, distTag );
                }
            }

        }

        bool PublishViaNPMProcess( IActivityMonitor m, MemoryStream uglyBuffer, string packageName, string distTag )
        {
            string tempDirectory = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
            using( m.OpenInfo( "Falling back on 'npm publish' external process." ) )
            {
                try
                {
                    Directory.CreateDirectory( tempDirectory );
                    using( StreamWriter w = File.CreateText( Path.Combine( tempDirectory, ".npmrc" ) ) )
                    {
                        string uriString = RegistryUri.ToString();
                        w.WriteLine( $"registry={uriString}" );
                        string uriConfig = uriString.Remove( 0, uriString.IndexOf( '/' ) );

                        if( _authHeader == null )
                        {
                            m.Error( "Missing credentials to configure npm.rc file." );
                            return false;
                        }
                        if( _authHeader.Scheme == "Basic" )
                        {
                            w.WriteLine( $"{uriConfig}:always-auth=true" );
                            w.WriteLine( $"{uriConfig}:_password={_password}" );
                            w.WriteLine( $"{uriConfig}:username={_username}" );
                        }
                        else if( _authHeader.Scheme == "Bearer" )
                        {
                            w.WriteLine( $"{uriConfig}:always-auth=true" );
                            w.WriteLine( $"{uriConfig}:_authToken={_authHeader.Parameter}" );
                        }

                        if( packageName.Contains( "@" ) )
                        {
                            w.WriteLine( packageName.Substring( 0, packageName.IndexOf( '/' ) ) + $":registry={RegistryUri.ToString()}" );
                        }
                    }
                    using( Stream stream = File.Create( Path.Combine( tempDirectory, "tarball.tgz" ) ) )
                    {
                        uglyBuffer.Position = 0;
                        uglyBuffer.CopyTo( stream );
                    }
                    string distTagArg = distTag != null ? $"--tag {distTag.ToLowerInvariant()}" : "";
                    return ProcessRunner.Run( m, tempDirectory, "cmd.exe", $"/C npm publish tarball.tgz {distTagArg}", LogLevel.Debug );
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

        async Task<(string body, HttpStatusCode statusCode)> ViewRequest( IActivityMonitor m, string endpoint, bool abreviated )
        {
            using( HttpRequestMessage req = NpmRequestMessage( m, endpoint, HttpMethod.Get ) )
            {
                if( abreviated )
                {
                    req.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/vnd.npm.install-v1+json", 1.0 ) );
                    req.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json", 0.8 ) );
                    req.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "*/*", 0.8 ) );
                }
                using( var response = await _httpClient.SendAsync( req ) )
                {
                    await HandleResponse( m, response );
                    var body = await response.Content.ReadAsStringAsync();
                    m.Debug( body );
                    return (body, response.StatusCode);
                }
            }
        }

        async Task<(string, bool)> LegacyViewWithVersion( IActivityMonitor m, string packageName, SVersion version, bool abreviated )
        {
            (string body, HttpStatusCode statusCode) = await ViewRequest( m, packageName, abreviated );
            if( !IsSuccessStatusCode( statusCode ) )
            {
                return (body, false);
            }
            // Here the request is successful so we should have valid json.
            JObject fullData = JObject.Parse( body );
            var v = fullData["versions"][version.ToNuGetPackageString()];
            return (v?.ToString() ?? "", v != null);
        }

        (string organization, string feedId) GetAzureInfoFromUri()
        {
            var match = new Regex(
                @"(?:https:\/\/pkgs\.dev\.azure\.com\/)([^\/]*)\/_packaging\/([^\/]*)\/npm\/registry" )
                    .Match( RegistryUri.ToString() );
            return (match.Groups[1].Value, match.Groups[2].Value);
        }

        bool IsAzureRepository()
        {
            return RegistryUri.Host == "pkgs.dev.azure.com";
        }

        async Task<(string body, bool found)> AzureSpecialVersionRequest( IActivityMonitor m, string packageName, string version )
        {
            (string organization, string feedId) = GetAzureInfoFromUri();
            string url = $"https://pkgs.dev.azure.com/" +
                $"{organization}/_apis/packaging/feeds/{feedId}/npm/{packageName}/versions/{version}?api-version=5.0-preview.1";
            using( HttpRequestMessage req = NpmRequestMessage( m, new Uri( url ), HttpMethod.Get ) )
            using( HttpResponseMessage res = await _httpClient.SendAsync( req ) )
            {
                string body = await res.Content.ReadAsStringAsync();
                if( res.StatusCode == HttpStatusCode.NotFound ) return (body, false);
                if( !res.IsSuccessStatusCode ) throw new Exception( "Status code not successfull and is not a 404" );
                return (body, true);
            }
        }

        public async Task<bool> ExistAsync( IActivityMonitor m, string packageName, SVersion version )
        {
            if( IsAzureRepository() )
            {
                (_, bool found) = await AzureSpecialVersionRequest( m, packageName, version.ToNuGetPackageString() );
                return found;
            }
            (_, bool exist) = await View( m, packageName, version );
            return exist;
        }

        /// <summary>
        /// Gets infos on a package.
        /// The json format is available here: https://github.com/npm/registry/blob/master/docs/responses/package-metadata.md
        /// </summary>
        /// <param name="m"></param>
        /// <param name="packageName">The package name</param>
        /// <param name="version">The info on the package on a specified versions, return a json containing the info of all the version if it's not specified</param>
        /// <param name="abbreviatedResponse">Ask the server to abreviate the info
        /// </param>
        /// <returns></returns>
        public async Task<(string viewJson, bool exist)> View( IActivityMonitor m, string packageName, SVersion version = null, bool abbreviatedResponse = true )
        {
            if( version == null )
            {
                // We can't optimise it unless we know the fallback feed, and we can't know with azure because the api ask for a version.
                m.Debug( "Getting all the versions of the package." );
                (string body, HttpStatusCode statusCode) = await ViewRequest( m, packageName, abbreviatedResponse );
                return (body, IsSuccessStatusCode( statusCode ));
            }
            // Here we know that the user specified the version
            return await LegacyViewWithVersion( m, packageName, version, abbreviatedResponse );
        }

        static bool IsSuccessStatusCode( HttpStatusCode statusCode )
        {
            return ((int)statusCode >= 200) && ((int)statusCode <= 299);
        }

        /// <summary>
        /// Create a request with the same header than npm would send.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="endpoint"></param>
        /// <param name="commandName"></param>
        /// <param name="projectScope"></param>
        /// <returns></returns>
        HttpRequestMessage NpmRequestMessage( IActivityMonitor m, string endpoint, HttpMethod method ) => NpmRequestMessage( m, new Uri( RegistryUri + endpoint ), method );

        HttpRequestMessage NpmRequestMessage( IActivityMonitor m, Uri fullUri, HttpMethod method )
        {
            var req = new HttpRequestMessage
            {
                RequestUri = fullUri,
                Method = method
            };
            m.Info( $"Request URI: {req.Method.ToString()}:'{req.RequestUri}'." );
            AddNpmHeaders( m, req.Headers );
            return req;
        }

        void AddNpmHeaders( IActivityMonitor m, HttpRequestHeaders headers )
        {
            const string userAgent = "Npm.Net/0.0.0";
            if( NpmInCi )
            {
                headers.Add( "npm-in-ci", NpmInCi.ToString().ToLower() ); //json type are lowercase
                m.Info( "Detected that we are running in CI. Sending to the registry an header indicating it." );
            }
            headers.Add( "npm-session", _session );
            headers.Add( "user-agent", userAgent );
            if( _authHeader != null )
            {
                headers.Authorization = _authHeader;
            }
        }

        async Task<bool> HandleResponse( IActivityMonitor m, HttpResponseMessage res )
        {
            HttpResponseHeaders headers = res.Headers;
            if( headers.Contains( "npm-notice" ) && !headers.Contains( "x-local-cache" ) )
            {
                m.Info( $"npm-notice headers: {headers.GetValues( "npm-notice" ).Concatenate()}" );
            }
            DumpWarnings( m, headers );
            return await LogErrors( m, res );
        }


        /// <summary>
        /// Logs potential errors. Returns true if no error occured, false on error.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="res"></param>
        /// <returns>True on success, false on error.</returns>
        async Task<bool> LogErrors( IActivityMonitor m, HttpResponseMessage res )
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
                            m.Error( "Unable to authenticate, need: " + string.Join( ",", auth ) );
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
                    string host = match.Groups[2].Value;
                    string message = match.Groups[3].Value;
                    DateTime date = JsonConvert.DeserializeObject<DateTime>( match.Groups[4].Value );
                    if( code == 199 )
                    {
                        if( message.Contains( "ENOTFOUND" ) )
                        {
                            m.Warn( $"registry: Using stale data from {RegistryUri.ToString()} because the host is inaccessible -- are you offline?" );
                            m.Error( "Npm.Net is not using any caches, so you should not see the previous warning." );
                        }
                        else
                        {
                            m.Warn( $"Unexpected warning for {RegistryUri.ToString()}: {message}" );
                        }
                    }
                    else if( code == 111 )
                    {
                        m.Warn( $"Using stale data from {RegistryUri.ToString()} due to a request error during revalidation." );
                    }
                }
            }
        }

        public override string ToString() => RegistryUri.ToString();
        public static JObject ExtractPackageJson( IActivityMonitor m, MemoryStream tarball )
        {
            var buffer = new byte[100];
            while( true )
            {
                tarball.Read( buffer, 0, 100 );
                var name = Encoding.ASCII.GetString( buffer ).Trim( ' ', '\0' );
                if( String.IsNullOrWhiteSpace( name ) )
                {
                    m.Error( "Tar entry 'package/packae.json' not found." );
                    return null;
                }
                tarball.Seek( 24, SeekOrigin.Current );
                tarball.Read( buffer, 0, 12 );
                var size = Convert.ToInt64( Encoding.ASCII.GetString( buffer, 0, 12 ).Trim( ' ', '\0' ), 8 );
                tarball.Seek( 376L, SeekOrigin.Current );
                if( name == "package/package.json" )
                {
                    m.Info( "Found 'package/package.json' in tarball" );
                    var bytes = new Byte[size];
                    tarball.Read( bytes, 0, bytes.Length );
                    var s = Encoding.UTF8.GetString( bytes );
                    return JObject.Parse( s );
                }
                else
                {
                    tarball.Seek( size, SeekOrigin.Current );
                }
                var pos = tarball.Position;
                var offset = 512 - (pos % 512);
                if( offset == 512 ) offset = 0;
                tarball.Seek( offset, SeekOrigin.Current );
            }
        }
    }
}
