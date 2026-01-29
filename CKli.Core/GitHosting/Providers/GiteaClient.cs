//using System;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using System.Net.Http.Json;
//using System.Text.Json;
//using System.Threading;
//using System.Threading.Tasks;
//using CK.Core;
//using CKli.Core.GitHosting.Models.Gitea;

//namespace CKli.Core.GitHosting.Providers;

///// <summary>
///// HTTP client for Gitea API operations.
///// </summary>
//internal sealed class GiteaClient : IDisposable
//{
//    static readonly JsonSerializerOptions s_jsonOptions = new()
//    {
//        PropertyNameCaseInsensitive = true
//    };

//    readonly HttpClient _httpClient;
//    readonly Uri _baseApiUrl;
//    readonly bool _ownsHttpClient;

//    /// <summary>
//    /// Creates a new Gitea client.
//    /// </summary>
//    /// <param name="pat">The Personal Access Token.</param>
//    /// <param name="baseApiUrl">The base API URL (e.g., https://gitea.company.com/api/v1).</param>
//    /// <param name="httpMessageHandler">Optional HTTP message handler for testing.</param>
//    public GiteaClient( string pat, Uri baseApiUrl, HttpMessageHandler? httpMessageHandler = null )
//    {
//        ArgumentException.ThrowIfNullOrWhiteSpace( pat );
//        ArgumentNullException.ThrowIfNull( baseApiUrl );

//        // Ensure base URL ends with / for proper relative URL resolution
//        _baseApiUrl = baseApiUrl.ToString().EndsWith( '/' )
//            ? baseApiUrl
//            : new Uri( baseApiUrl.ToString() + "/" );
//        _ownsHttpClient = httpMessageHandler == null;
//        _httpClient = httpMessageHandler != null
//            ? new HttpClient( httpMessageHandler )
//            : new HttpClient();

//        _httpClient.BaseAddress = _baseApiUrl;
//        _httpClient.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json" ) );
//        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "token", pat );
//        _httpClient.DefaultRequestHeaders.UserAgent.Add( new ProductInfoHeaderValue( "CKli-GitHosting", "1.0" ) );
//    }

//    /// <summary>
//    /// Gets repository information.
//    /// </summary>
//    public async Task<GitHostingOperationResult<HostedRepositoryInfo>> GetRepositoryAsync(
//        IActivityMonitor monitor,
//        string owner,
//        string repoName,
//        CancellationToken ct = default )
//    {
//        try
//        {
//            var response = await _httpClient.GetAsync( $"repos/{owner}/{repoName}", ct );
//            return await HandleResponseAsync<HostedRepositoryInfo>( monitor, response, MapToRepositoryInfo, ct );
//        }
//        catch( Exception ex )
//        {
//            monitor.Error( $"Failed to get repository {owner}/{repoName}", ex );
//            return GitHostingOperationResult<HostedRepositoryInfo>.Fail( ex.Message );
//        }
//    }

//    /// <summary>
//    /// Creates a new repository.
//    /// </summary>
//    public async Task<GitHostingOperationResult<HostedRepositoryInfo>> CreateRepositoryAsync(
//        IActivityMonitor monitor,
//        HostedRepositoryCreateOptions options,
//        CancellationToken ct = default )
//    {
//        try
//        {
//            var request = new GiteaCreateRepoRequest
//            {
//                Name = "should be repoPath",
//                Description = options.Description,
//                Private = options.IsPrivate,
//                AutoInit = options.AutoInit,
//                Gitignores = options.GitIgnoreTemplate,
//                License = options.LicenseTemplate
//            };

//            // Try organization endpoint first
//            var url = $"orgs/{options.Owner}/repos";
//            var response = await _httpClient.PostAsJsonAsync( url, request, s_jsonOptions, ct );

//            // If org endpoint fails (404 Not Found, 405 Method Not Allowed, or other client errors),
//            // fall back to user repos endpoint (creates repo for authenticated user)
//            if( response.StatusCode == System.Net.HttpStatusCode.NotFound ||
//                response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed ||
//                response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity )
//            {
//                url = "user/repos";
//                response = await _httpClient.PostAsJsonAsync( url, request, s_jsonOptions, ct );
//            }

//            return await HandleResponseAsync<HostedRepositoryInfo>( monitor, response, MapToRepositoryInfo, ct );
//        }
//        catch( Exception ex )
//        {
//            monitor.Error( $"Failed to create repository {options.Owner}/{options.Name}", ex );
//            return GitHostingOperationResult<HostedRepositoryInfo>.Fail( ex.Message );
//        }
//    }

//    /// <summary>
//    /// Archives a repository.
//    /// </summary>
//    public async Task<GitHostingOperationResult> ArchiveRepositoryAsync(
//        IActivityMonitor monitor,
//        string owner,
//        string repoName,
//        CancellationToken ct = default )
//    {
//        try
//        {
//            var request = new GiteaUpdateRepoRequest { Archived = true };
//            var response = await _httpClient.PatchAsJsonAsync( $"repos/{owner}/{repoName}", request, s_jsonOptions, ct );

//            if( response.IsSuccessStatusCode )
//            {
//                return GitHostingOperationResult.Ok();
//            }

//            var error = await ParseErrorAsync( response, ct );
//            return GitHostingOperationResult.Fail( error, (int)response.StatusCode );
//        }
//        catch( Exception ex )
//        {
//            monitor.Error( $"Failed to archive repository {owner}/{repoName}", ex );
//            return GitHostingOperationResult.Fail( ex.Message );
//        }
//    }

//    /// <summary>
//    /// Deletes a repository.
//    /// </summary>
//    public async Task<GitHostingOperationResult> DeleteRepositoryAsync(
//        IActivityMonitor monitor,
//        string owner,
//        string repoName,
//        CancellationToken ct = default )
//    {
//        try
//        {
//            var response = await _httpClient.DeleteAsync( $"repos/{owner}/{repoName}", ct );

//            if( response.IsSuccessStatusCode )
//            {
//                return GitHostingOperationResult.Ok();
//            }

//            var error = await ParseErrorAsync( response, ct );
//            return GitHostingOperationResult.Fail( error, (int)response.StatusCode );
//        }
//        catch( Exception ex )
//        {
//            monitor.Error( $"Failed to delete repository {owner}/{repoName}", ex );
//            return GitHostingOperationResult.Fail( ex.Message );
//        }
//    }

//    async Task<GitHostingOperationResult<TResult>> HandleResponseAsync<TResult>(
//        IActivityMonitor monitor,
//        HttpResponseMessage response,
//        Func<GiteaRepository, TResult> mapper,
//        CancellationToken ct )
//    {
//        if( response.IsSuccessStatusCode )
//        {
//            var repo = await response.Content.ReadFromJsonAsync<GiteaRepository>( s_jsonOptions, ct );
//            if( repo == null )
//            {
//                return GitHostingOperationResult<TResult>.Fail( "Empty response from Gitea API" );
//            }
//            return GitHostingOperationResult<TResult>.Ok( mapper( repo ) );
//        }

//        var error = await ParseErrorAsync( response, ct );
//        return GitHostingOperationResult<TResult>.Fail( error, (int)response.StatusCode );
//    }

//    async Task<string> ParseErrorAsync( HttpResponseMessage response, CancellationToken ct )
//    {
//        try
//        {
//            var content = await response.Content.ReadAsStringAsync( ct );
//            var error = JsonSerializer.Deserialize<GiteaErrorResponse>( content, s_jsonOptions );

//            if( error?.Message != null )
//            {
//                return error.Message;
//            }
//            return $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
//        }
//        catch
//        {
//            return $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
//        }
//    }

//    static HostedRepositoryInfo MapToRepositoryInfo( GiteaRepository repo )
//    {
//        return new HostedRepositoryInfo
//        {
//            Owner = repo.Owner?.Login ?? repo.FullName.Split( '/' )[0],
//            Name = repo.Name,
//            Description = repo.Description,
//            IsPrivate = repo.Private,
//            IsArchived = repo.Archived,
//            IsEmpty = repo.Empty,
//            DefaultBranch = repo.DefaultBranch,
//            CloneUrlHttps = repo.CloneUrl,
//            CloneUrlSsh = repo.SshUrl,
//            WebUrl = repo.HtmlUrl,
//            CreatedAt = repo.CreatedAt,
//            UpdatedAt = repo.UpdatedAt
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
