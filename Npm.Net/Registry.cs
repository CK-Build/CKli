using CK.Core;
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
        readonly string _session;

        bool _thisRegistryIsModern;
        /// <summary>
        /// Simple cache that knows that a registry have newer api endpoint or not.
        /// </summary>
        static (string registryHost, bool isUpToDate)[] _modernRegistries = new (string registryHost, bool isUpToDate)[]
        {
            (_npmjs.Host, true)
        };

        /// <summary>
        /// Create a registry that make unauthenticated request.
        /// </summary>
        /// <param name="httpClient">The <see cref="HttpClient"/> used to send the requests</param>
        /// <param name="registryUri">Registry url, https://registry.npmjs.org/ is used if the uri is not specified </param>
        public Registry( HttpClient httpClient, Uri registryUri = null )
        {
            RegistryUri = registryUri ?? _npmjs;
            InitRegistryUpToDate();
            _httpClient = httpClient;
            _session = GenerateSessionId();
        }

        /// <summary>
        /// Create a registry that make authenticated request
        /// </summary>
        /// <param name="httpClient">The <see cref="HttpClient"/> used to send the requests</param>
        /// <param name="registryUri">Registry url, https://registry.npmjs.org/ is used if the uri is not specified </param>
        /// <param name="token">Bearer token used to authenticate.</param>
        public Registry( HttpClient httpClient, string token, Uri registryUri = null )
            : this( httpClient, registryUri )
        {
            _authHeader = new AuthenticationHeaderValue( "Bearer", token );
        }

        /// <summary>
        /// Create a registry that make authenticated request
        /// </summary>
        /// <param name="httpClient">The <see cref="HttpClient"/> used to send the requests</param>
        /// <param name="registryUri">Registry <see cref="Uri"/>, https://registry.npmjs.org/ is used if the uri is not specified </param>
        /// <param name="username">Username used for the authentification.</param>
        /// <param name="password">
        /// Password used for the authentification.
        /// Npm base64 decode the password, we don't, so you must do it yourself.
        /// </param>
        public Registry( HttpClient httpClient, string username, string password, Uri registryUri = null )
            : this( httpClient, registryUri )
        {
            string basic = Convert.ToBase64String( Encoding.ASCII.GetBytes( $"{username}:{password}" ) );
            _authHeader = new AuthenticationHeaderValue( "Basic", basic );
        }


        static (string registryUri, bool isUpToDate)? GetModernRegistry( string registryHost ) => _modernRegistries.FirstOrDefault( e => e.registryHost == registryHost );

        /// <summary>
        /// Return whether the registry is up to date. If we never tested a repository we are optimistic
        /// and consider that the registry have recent endpoint
        /// </summary>
        /// <param name="registryHost"></param>
        /// <returns></returns>
        static bool IsModernRegistry( string registryHost ) => GetModernRegistry( registryHost )?.isUpToDate ?? true;

        static void TryAddRegistryWithStatus( IActivityMonitor m, string registryHost, bool isUpToDate )
        {
            (string registryUri, bool isUpToDate)? valueTuple = GetModernRegistry( registryHost );
            if( valueTuple == null )
            {
                Util.InterlockedAdd( ref _modernRegistries, (registryHost, isUpToDate) );
                if( isUpToDate )
                {
                    m.Debug( "Request on recent endpoint works: I will never try legacy methods on it." );
                }
                else
                {
                    m.Debug( "This registry does not support recent endpoint. We will now stop to try to access the recent endpoints" );
                }
            }
            else if( !valueTuple.Value.isUpToDate )
            {
                m.Error( "Repository was recorded as not up to date, but i tried to add it as up to date. " );
            }
        }

        void InitRegistryUpToDate()
        {
            if( IsAzureRepository() )
            {
                _thisRegistryIsModern = false;
            }
            else
            {
                _thisRegistryIsModern = IsModernRegistry( RegistryUri.Host );
            }
        }


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
        /// Gets the Uri of the registry.
        /// </summary>
        public Uri RegistryUri { get; }

        /// <summary>
        /// Gets or sets wether we are running in CI or not.
        /// The fields is automatically set based on the environement variables with the same behavior as npm.
        /// </summary>
        public bool NpmInCi { get; set; }
            = Environment.GetEnvironmentVariable( "CI" ) == "true"
                || Environment.GetEnvironmentVariable( "TDDIUM" ) != null
                || Environment.GetEnvironmentVariable( "JENKINS_URL" ) != null
                || Environment.GetEnvironmentVariable( "bamboo.buildKey" ) != null
                || Environment.GetEnvironmentVariable( "GO_PIPELINE_NAME" ) != null;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="m"></param>
        /// <param name="packageName"></param>
        /// <returns>The dist-tags on this package</returns>
        public async Task<string> GetDistTags( IActivityMonitor m, string packageName )
        {
            using( HttpRequestMessage req = NpmRequestMessage( m, $"/-/package/{packageName}/dist-tags", HttpMethod.Get ) )
            using( HttpResponseMessage response = await _httpClient.SendAsync( req ) )
            {
                await LogResponse( m, response );
                return await response.Content.ReadAsStringAsync();
            }
        }

        public async Task<bool> AddDistTag( IActivityMonitor m, string packageName, SVersion version, string tagName )
        {
            using( HttpRequestMessage req = NpmRequestMessage( m, $"/-/package/{packageName}/dist-tags/{tagName}", HttpMethod.Put ) )
            {
                string a = "\"" + version.ToString() + "\"";
                req.Content = new StringContent( "\"" + version.ToString() + "\"" );
                req.Content.Headers.ContentType.MediaType = "application/json";
                req.Content.Headers.ContentType.CharSet = "";
                using( HttpResponseMessage response = await _httpClient.SendAsync( req ) )
                {
                    if( !await LogResponse( m, response ) ) return false;
                }
            }
            return true;
        }

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

        /// <summary>
        /// Publish a package to the repository
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageJson">The package.json of the package to push</param>
        /// <param name="packagePathTarGz">
        /// Physical file path to the package tar gz file to push.
        /// </param>
        /// <param name="distTag">First optional tag to be associated to the package.</param>
        /// <returns>True on success, false on error.</returns>
        public async Task<bool> PublishAsync( IActivityMonitor m, string packagePathTarGz, string distTag = null )
        {
            JObject packageJson;
            using( MemoryStream tarball = new MemoryStream() )
            using( var file = File.OpenRead( packagePathTarGz ) )
            using( var gz = new GZipStream( file, CompressionMode.Decompress, leaveOpen: true ) )
            {
                await gz.CopyToAsync( tarball );
                tarball.Position = 0;
                packageJson = ExtractPackageJson( m, tarball );
            }

            using( m.OpenInfo( $"Pushing {packagePathTarGz} {(distTag != null ? $"with disTag='{distTag}'" : "")} to '{RegistryUri}'." ) )
            {
                using( var tarball = new MemoryStream() )
                using( FileStream file = File.OpenRead( packagePathTarGz ) )
                using( var gz = new GZipStream( file, CompressionMode.Decompress, leaveOpen: true ) )
                {
                    await gz.CopyToAsync( tarball );
                }
                if( packageJson == null ) return false;
                using( FileStream file = File.OpenRead( packagePathTarGz ) )
                using( HttpRequestMessage req = NpmRequestMessage( m, packageJson["name"].ToString(), HttpMethod.Put ) )
                using( MetadataStream metadataStream = MetadataStream.LegacyMetadataStream( m, RegistryUri, packageJson, file, distTag ) )
                {
                    req.Content = metadataStream;
                    /**
                     * npm does more things than we do:
                     * if the first request return 409: conflict, npm fetch the versions availables in the registry.
                     * With these versions npm patch the metadata and resend a packet.
                     * We think it's probably for legacy reason, the simple request works on Azure Devops and Verdaccio
                     * https://github.com/npm/npm-registry-client/commit/e9fbeb8b67f249394f735c74ef11fe4720d46ca0
                     * TL;DR: The legacy npm publish is not implemented.
                     **/
                    using( var response = await _httpClient.SendAsync( req ) )
                    {
                        return await LogResponse( m, response );
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
                    await LogResponse( m, response );
                    return (await response.Content.ReadAsStringAsync(), response.StatusCode);
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
            m.Info( $"This registry doesn't implement the /version endpoint. Looking for {version} in all the versions." );
            m.Debug( body );
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
                (_, bool found) = await AzureSpecialVersionRequest( m, packageName, version.ToString() );
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
            if( !_thisRegistryIsModern )
            {
                if( IsAzureRepository() )
                {
                    (string body, bool found) = await AzureSpecialVersionRequest( m, packageName, version.ToString() );
                    if( !found )
                    {
                        m.Info( "Azure API indicated that this version does not exist" );
                        return ("", false);
                    }
                    JObject json = JObject.Parse( body );
                    if( json["sourceChain"] is JArray sourceChain )
                    {
                        List<JToken> validRepository = sourceChain.Values()
                            .Where(
                            p => p["sourceType"]?.ToString() == "public"
                            && Uri.TryCreate( p["location"]?.ToString(), UriKind.Absolute, out Uri result )
                            && IsModernRegistry( result.Host ) ).ToList();
                        if( validRepository.Count == 1 )
                        {
                            var sourceRepository = new Registry( _httpClient, validRepository.Single()["location"].ToString() );
                            m.Debug( "Source repository have probably an api more up to date than azure, we ask it the data" );
                            return await sourceRepository.View( m, packageName, version );
                        }
                        //We won't try if we get multiple source feeds, knowing where is the package is up to azure.
                    }
                }
                m.Debug( "Registry is not modern, using Legacy methods." );
                return await LegacyViewWithVersion( m, packageName, version, abbreviatedResponse );
            }
            else
            {
                if( RegistryUri.Host == _npmjs.Host )
                {
                    m.Debug( $"{_npmjs.Host} doesn't support /version queries, using Legacy methods." );
                    return await LegacyViewWithVersion( m, packageName, version, abbreviatedResponse );
                }
                // Registry is probably up to date.
                string uriWithVersion = packageName += "/" + version;
                (string body, HttpStatusCode statusCode) = await ViewRequest( m, uriWithVersion, abbreviatedResponse );
                if( IsSuccessStatusCode( statusCode ) )
                {
                    m.Info( "Request successful." );
                    TryAddRegistryWithStatus( m, RegistryUri.Host, true );

                    return (body, true);
                }
                // Here the request have an error: but the repository is maybe not up to date and not in the cache!
                if( GetModernRegistry( RegistryUri.Host ) == null )
                {
                    m.Debug( "Unsure if repository is up to date. Retrying with legacy endpoint" );
                    // Ok we never tested this registry and we were optimistic.
                    (string filteredBody, bool foundPackage) = await LegacyViewWithVersion( m, packageName, version, abbreviatedResponse );
                    if( !foundPackage )
                    {
                        m.Info( "This package does not exist." );
                        //Worst case, we don't know if the package exist and if the repository support new endpoints
                        return (filteredBody, false);
                    }

                    m.Info( "Request successful" );
                    _thisRegistryIsModern = false;
                    TryAddRegistryWithStatus( m, RegistryUri.Host, false );
                    return (filteredBody, true);
                }
                //We know that the host is a modern registry, it should throw 404 if the versions does not exist.
                if( statusCode == HttpStatusCode.NotFound )
                {
                    m.Info( "This version or package does not exist." );
                    return (body, false);
                }
                else
                {
                    throw new Exception( $"HttpStatusCode is not success or 404, status code:'{statusCode.ToString()}'." +
                        $"\r\n Message body: \r\n {body}" );
                }
            }
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

        async Task<bool> LogResponse( IActivityMonitor m, HttpResponseMessage res )
        {
            m.Info( "Checking response" );
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
    }
}
