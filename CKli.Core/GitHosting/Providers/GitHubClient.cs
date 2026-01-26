using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CK.Core;
using CKli.Core.GitHosting.Models.GitHub;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// HTTP client for GitHub API operations.
/// </summary>
internal sealed class GitHubClient : IDisposable
{
    static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    readonly HttpClient _httpClient;
    readonly Uri _baseApiUrl;
    readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a new GitHub client.
    /// </summary>
    /// <param name="pat">The Personal Access Token.</param>
    /// <param name="baseApiUrl">The base API URL (default: https://api.github.com).</param>
    /// <param name="httpMessageHandler">Optional HTTP message handler for testing.</param>
    public GitHubClient( string pat, Uri? baseApiUrl = null, HttpMessageHandler? httpMessageHandler = null )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( pat );

        var url = baseApiUrl ?? new Uri( "https://api.github.com" );
        // Ensure base URL ends with / for proper relative URL resolution
        _baseApiUrl = url.ToString().EndsWith( '/' )
            ? url
            : new Uri( url.ToString() + "/" );
        _ownsHttpClient = httpMessageHandler == null;
        _httpClient = httpMessageHandler != null
            ? new HttpClient( httpMessageHandler )
            : new HttpClient();

        _httpClient.BaseAddress = _baseApiUrl;
        _httpClient.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/vnd.github+json" ) );
        _httpClient.DefaultRequestHeaders.Add( "X-GitHub-Api-Version", "2022-11-28" );
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", pat );
        _httpClient.DefaultRequestHeaders.UserAgent.Add( new ProductInfoHeaderValue( "CKli-GitHosting", "1.0" ) );
    }

    /// <summary>
    /// Gets repository information.
    /// </summary>
    public async Task<GitHostingOperationResult<RepositoryInfo>> GetRepositoryAsync(
        IActivityMonitor monitor,
        string owner,
        string repoName,
        CancellationToken ct = default )
    {
        try
        {
            var response = await _httpClient.GetAsync( $"repos/{owner}/{repoName}", ct );
            var result = await HandleResponseAsync<RepositoryInfo>( monitor, response, MapToRepositoryInfo, ct );

            if( result.Success && result.Data != null )
            {
                // Check if repo is empty by checking if any refs exist
                var isEmpty = await CheckIsEmptyAsync( owner, repoName, ct );
                return GitHostingOperationResult<RepositoryInfo>.Ok( result.Data with { IsEmpty = isEmpty } );
            }

            return result;
        }
        catch( Exception ex )
        {
            monitor.Error( $"Failed to get repository {owner}/{repoName}", ex );
            return GitHostingOperationResult<RepositoryInfo>.Fail( ex.Message );
        }
    }

    /// <summary>
    /// Checks if a repository is empty (has no commits/refs).
    /// </summary>
    async Task<bool> CheckIsEmptyAsync( string owner, string repoName, CancellationToken ct )
    {
        try
        {
            // GET /repos/{owner}/{repo}/git/refs/heads returns:
            // - 409 Conflict with message "Git Repository is empty." for empty repos
            // - 404 Not Found in some edge cases
            // - 200 OK with array of refs for non-empty repos
            var response = await _httpClient.GetAsync( $"repos/{owner}/{repoName}/git/refs/heads", ct );
            return response.StatusCode == System.Net.HttpStatusCode.Conflict
                || response.StatusCode == System.Net.HttpStatusCode.NotFound;
        }
        catch
        {
            // If we can't determine, assume not empty to be safe
            return false;
        }
    }

    /// <summary>
    /// Creates a new repository.
    /// </summary>
    public async Task<GitHostingOperationResult<RepositoryInfo>> CreateRepositoryAsync(
        IActivityMonitor monitor,
        RepositoryCreateOptions options,
        CancellationToken ct = default )
    {
        try
        {
            var request = new GitHubCreateRepoRequest
            {
                Name = options.Name,
                Description = options.Description,
                Private = options.IsPrivate,
                AutoInit = options.AutoInit,
                GitignoreTemplate = options.GitIgnoreTemplate,
                LicenseTemplate = options.LicenseTemplate
            };

            // Determine if we're creating in an org or for the authenticated user
            // If owner matches authenticated user, use user/repos
            // Otherwise, use /orgs/{org}/repos
            var url = $"orgs/{options.Owner}/repos";

            var response = await _httpClient.PostAsJsonAsync( url, request, s_jsonOptions, ct );

            // If 404 on org endpoint, the owner might be a user - try user repos endpoint
            if( response.StatusCode == System.Net.HttpStatusCode.NotFound )
            {
                // For user repos, we use user/repos endpoint
                // This requires the owner to be the authenticated user
                url = "user/repos";
                response = await _httpClient.PostAsJsonAsync( url, request, s_jsonOptions, ct );
            }

            return await HandleResponseAsync<RepositoryInfo>( monitor, response, MapToRepositoryInfo, ct );
        }
        catch( Exception ex )
        {
            monitor.Error( $"Failed to create repository {options.Owner}/{options.Name}", ex );
            return GitHostingOperationResult<RepositoryInfo>.Fail( ex.Message );
        }
    }

    /// <summary>
    /// Archives a repository.
    /// </summary>
    public async Task<GitHostingOperationResult> ArchiveRepositoryAsync(
        IActivityMonitor monitor,
        string owner,
        string repoName,
        CancellationToken ct = default )
    {
        try
        {
            var request = new GitHubUpdateRepoRequest { Archived = true };
            var response = await _httpClient.PatchAsJsonAsync( $"repos/{owner}/{repoName}", request, s_jsonOptions, ct );

            if( response.IsSuccessStatusCode )
            {
                return GitHostingOperationResult.Ok();
            }

            var error = await ParseErrorAsync( response, ct );
            return GitHostingOperationResult.Fail( error, (int)response.StatusCode );
        }
        catch( Exception ex )
        {
            monitor.Error( $"Failed to archive repository {owner}/{repoName}", ex );
            return GitHostingOperationResult.Fail( ex.Message );
        }
    }

    /// <summary>
    /// Deletes a repository.
    /// </summary>
    public async Task<GitHostingOperationResult> DeleteRepositoryAsync(
        IActivityMonitor monitor,
        string owner,
        string repoName,
        CancellationToken ct = default )
    {
        try
        {
            var response = await _httpClient.DeleteAsync( $"repos/{owner}/{repoName}", ct );

            if( response.IsSuccessStatusCode )
            {
                return GitHostingOperationResult.Ok();
            }

            var error = await ParseErrorAsync( response, ct );
            return GitHostingOperationResult.Fail( error, (int)response.StatusCode );
        }
        catch( Exception ex )
        {
            monitor.Error( $"Failed to delete repository {owner}/{repoName}", ex );
            return GitHostingOperationResult.Fail( ex.Message );
        }
    }

    async Task<GitHostingOperationResult<TResult>> HandleResponseAsync<TResult>(
        IActivityMonitor monitor,
        HttpResponseMessage response,
        Func<GitHubRepository, TResult> mapper,
        CancellationToken ct )
    {
        if( response.IsSuccessStatusCode )
        {
            var repo = await response.Content.ReadFromJsonAsync<GitHubRepository>( s_jsonOptions, ct );
            if( repo == null )
            {
                return GitHostingOperationResult<TResult>.Fail( "Empty response from GitHub API" );
            }
            return GitHostingOperationResult<TResult>.Ok( mapper( repo ) );
        }

        var error = await ParseErrorAsync( response, ct );
        return GitHostingOperationResult<TResult>.Fail( error, (int)response.StatusCode );
    }

    async Task<string> ParseErrorAsync( HttpResponseMessage response, CancellationToken ct )
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync( ct );
            var error = JsonSerializer.Deserialize<GitHubErrorResponse>( content, s_jsonOptions );

            if( error?.Message != null )
            {
                if( error.Errors?.Count > 0 )
                {
                    var details = string.Join( "; ", error.Errors.Select( e => e.Message ?? e.Code ?? "Unknown error" ) );
                    return $"{error.Message}: {details}";
                }
                return error.Message;
            }
            return $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
        }
        catch
        {
            return $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
        }
    }

    static RepositoryInfo MapToRepositoryInfo( GitHubRepository repo )
    {
        return new RepositoryInfo
        {
            Owner = repo.Owner?.Login ?? repo.FullName.Split( '/' )[0],
            Name = repo.Name,
            Description = repo.Description,
            IsPrivate = repo.Private,
            IsArchived = repo.Archived,
            DefaultBranch = repo.DefaultBranch,
            CloneUrlHttps = repo.CloneUrl,
            CloneUrlSsh = repo.SshUrl,
            WebUrl = repo.HtmlUrl,
            CreatedAt = repo.CreatedAt,
            UpdatedAt = repo.UpdatedAt
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if( _ownsHttpClient )
        {
            _httpClient.Dispose();
        }
    }
}
