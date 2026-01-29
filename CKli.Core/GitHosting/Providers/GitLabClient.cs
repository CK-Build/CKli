//using System;
//using System.Linq;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using System.Net.Http.Json;
//using System.Text.Json;
//using System.Threading;
//using System.Threading.Tasks;
//using System.Web;
//using CK.Core;
//using CKli.Core.GitHosting.Models.GitLab;

//namespace CKli.Core.GitHosting.Providers;

///// <summary>
///// HTTP client for GitLab API operations.
///// </summary>
//internal sealed class GitLabClient : IDisposable
//{
//    static readonly JsonSerializerOptions s_jsonOptions = new()
//    {
//        PropertyNameCaseInsensitive = true
//    };

//    readonly HttpClient _httpClient;
//    readonly Uri _baseApiUrl;
//    readonly bool _ownsHttpClient;

//    /// <summary>
//    /// Creates a new GitLab client.
//    /// </summary>
//    /// <param name="pat">The Personal Access Token.</param>
//    /// <param name="baseApiUrl">The base API URL (default: https://gitlab.com/api/v4).</param>
//    /// <param name="httpMessageHandler">Optional HTTP message handler for testing.</param>
//    public GitLabClient( string pat, Uri? baseApiUrl = null, HttpMessageHandler? httpMessageHandler = null )
//    {
//        ArgumentException.ThrowIfNullOrWhiteSpace( pat );

//        var url = baseApiUrl ?? new Uri( "https://gitlab.com/api/v4" );
//        // Ensure base URL ends with / for proper relative URL resolution
//        _baseApiUrl = url.ToString().EndsWith( '/' )
//            ? url
//            : new Uri( url.ToString() + "/" );
//        _ownsHttpClient = httpMessageHandler == null;
//        _httpClient = httpMessageHandler != null
//            ? new HttpClient( httpMessageHandler )
//            : new HttpClient();

//        _httpClient.BaseAddress = _baseApiUrl;
//        _httpClient.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json" ) );
//        _httpClient.DefaultRequestHeaders.Add( "PRIVATE-TOKEN", pat );
//        _httpClient.DefaultRequestHeaders.UserAgent.Add( new ProductInfoHeaderValue( "CKli-GitHosting", "1.0" ) );
//    }

//    /// <summary>
//    /// Gets project information.
//    /// </summary>
//    public async Task<GitHostingOperationResult<HostedRepositoryInfo>> GetProjectAsync(
//        IActivityMonitor monitor,
//        string owner,
//        string repoName,
//        CancellationToken ct = default )
//    {
//        try
//        {
//            // GitLab uses URL-encoded project path: group%2Fsubgroup%2Frepo
//            var projectPath = HttpUtility.UrlEncode( $"{owner}/{repoName}" );
//            var response = await _httpClient.GetAsync( $"projects/{projectPath}", ct );
//            return await HandleResponseAsync<HostedRepositoryInfo>( monitor, response, MapToRepositoryInfo, ct );
//        }
//        catch( Exception ex )
//        {
//            monitor.Error( $"Failed to get project {owner}/{repoName}", ex );
//            return GitHostingOperationResult<HostedRepositoryInfo>.Fail( ex.Message );
//        }
//    }

//    /// <summary>
//    /// Creates a new project.
//    /// </summary>
//    public async Task<GitHostingOperationResult<HostedRepositoryInfo>> CreateProjectAsync(
//        IActivityMonitor monitor,
//        HostedRepositoryCreateOptions options,
//        CancellationToken ct = default )
//    {
//        try
//        {
//            // First, try to get the namespace ID for the owner
//            long? namespaceId = await GetNamespaceIdAsync( options.Owner, ct );

//            var request = new GitLabCreateProjectRequest
//            {
//                Name = options.Name,
//                Description = options.Description,
//                Visibility = options.IsPrivate ? "private" : "public",
//                NamespaceId = namespaceId,
//                InitializeWithReadme = options.AutoInit
//            };

//            var response = await _httpClient.PostAsJsonAsync( "projects", request, s_jsonOptions, ct );
//            return await HandleResponseAsync<HostedRepositoryInfo>( monitor, response, MapToRepositoryInfo, ct );
//        }
//        catch( Exception ex )
//        {
//            monitor.Error( $"Failed to create project {options.Owner}/{options.Name}", ex );
//            return GitHostingOperationResult<HostedRepositoryInfo>.Fail( ex.Message );
//        }
//    }

//    /// <summary>
//    /// Gets the namespace ID for a group or user path.
//    /// </summary>
//    async Task<long?> GetNamespaceIdAsync( string namespacePath, CancellationToken ct )
//    {
//        try
//        {
//            var encodedPath = HttpUtility.UrlEncode( namespacePath );
//            var response = await _httpClient.GetAsync( $"namespaces/{encodedPath}", ct );

//            if( response.IsSuccessStatusCode )
//            {
//                var ns = await response.Content.ReadFromJsonAsync<GitLabNamespace>( s_jsonOptions, ct );
//                return ns?.Id;
//            }
//        }
//        catch
//        {
//            // If namespace lookup fails, project creation will use current user's namespace
//        }
//        return null;
//    }

//    /// <summary>
//    /// Archives a project.
//    /// </summary>
//    public async Task<GitHostingOperationResult> ArchiveProjectAsync(
//        IActivityMonitor monitor,
//        string owner,
//        string repoName,
//        CancellationToken ct = default )
//    {
//        try
//        {
//            var projectPath = HttpUtility.UrlEncode( $"{owner}/{repoName}" );
//            var response = await _httpClient.PostAsync( $"projects/{projectPath}/archive", null, ct );

//            if( response.IsSuccessStatusCode )
//            {
//                return GitHostingOperationResult.Ok();
//            }

//            var error = await ParseErrorAsync( response, ct );
//            return GitHostingOperationResult.Fail( error, (int)response.StatusCode );
//        }
//        catch( Exception ex )
//        {
//            monitor.Error( $"Failed to archive project {owner}/{repoName}", ex );
//            return GitHostingOperationResult.Fail( ex.Message );
//        }
//    }

//    /// <summary>
//    /// Deletes a project.
//    /// </summary>
//    public async Task<GitHostingOperationResult> DeleteProjectAsync(
//        IActivityMonitor monitor,
//        string owner,
//        string repoName,
//        CancellationToken ct = default )
//    {
//        try
//        {
//            var projectPath = HttpUtility.UrlEncode( $"{owner}/{repoName}" );
//            var response = await _httpClient.DeleteAsync( $"projects/{projectPath}", ct );

//            if( response.IsSuccessStatusCode )
//            {
//                return GitHostingOperationResult.Ok();
//            }

//            var error = await ParseErrorAsync( response, ct );
//            return GitHostingOperationResult.Fail( error, (int)response.StatusCode );
//        }
//        catch( Exception ex )
//        {
//            monitor.Error( $"Failed to delete project {owner}/{repoName}", ex );
//            return GitHostingOperationResult.Fail( ex.Message );
//        }
//    }

//    async Task<GitHostingOperationResult<TResult>> HandleResponseAsync<TResult>(
//        IActivityMonitor monitor,
//        HttpResponseMessage response,
//        Func<GitLabProject, TResult> mapper,
//        CancellationToken ct )
//    {
//        if( response.IsSuccessStatusCode )
//        {
//            var project = await response.Content.ReadFromJsonAsync<GitLabProject>( s_jsonOptions, ct );
//            if( project == null )
//            {
//                return GitHostingOperationResult<TResult>.Fail( "Empty response from GitLab API" );
//            }
//            return GitHostingOperationResult<TResult>.Ok( mapper( project ) );
//        }

//        var error = await ParseErrorAsync( response, ct );
//        return GitHostingOperationResult<TResult>.Fail( error, (int)response.StatusCode );
//    }

//    async Task<string> ParseErrorAsync( HttpResponseMessage response, CancellationToken ct )
//    {
//        try
//        {
//            var content = await response.Content.ReadAsStringAsync( ct );
//            var error = JsonSerializer.Deserialize<GitLabErrorResponse>( content, s_jsonOptions );

//            // GitLab error responses can have message as string or object
//            if( error?.Message != null )
//            {
//                if( error.Message is JsonElement element )
//                {
//                    if( element.ValueKind == JsonValueKind.String )
//                    {
//                        return element.GetString() ?? $"HTTP {(int)response.StatusCode}";
//                    }
//                    if( element.ValueKind == JsonValueKind.Object || element.ValueKind == JsonValueKind.Array )
//                    {
//                        return element.GetRawText();
//                    }
//                }
//                return error.Message.ToString() ?? $"HTTP {(int)response.StatusCode}";
//            }
//            if( error?.Error != null )
//            {
//                var desc = error.ErrorDescription != null ? $": {error.ErrorDescription}" : "";
//                return $"{error.Error}{desc}";
//            }
//            return $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
//        }
//        catch
//        {
//            return $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
//        }
//    }

//    static HostedRepositoryInfo MapToRepositoryInfo( GitLabProject project )
//    {
//        // Extract owner from path_with_namespace (e.g., "group/subgroup/repo" -> "group/subgroup")
//        var pathParts = project.PathWithNamespace.Split( '/' );
//        var owner = pathParts.Length > 1
//            ? string.Join( "/", pathParts.Take( pathParts.Length - 1 ) )
//            : project.Namespace?.FullPath ?? "";

//        return new HostedRepositoryInfo
//        {
//            Owner = owner,
//            Name = project.Path,
//            Description = project.Description,
//            IsPrivate = project.Visibility != "public",
//            IsArchived = project.Archived,
//            IsEmpty = project.EmptyRepo,
//            DefaultBranch = project.DefaultBranch,
//            CloneUrlHttps = project.HttpUrlToRepo,
//            CloneUrlSsh = project.SshUrlToRepo,
//            WebUrl = project.WebUrl,
//            CreatedAt = project.CreatedAt,
//            UpdatedAt = project.LastActivityAt
//        };
//    }

//    /// <inheritdoc />
//    public void Dispose()
//    {
//        if( _ownsHttpClient )
//        {
//            _httpClient.Dispose();
//        }
//    }
//}
