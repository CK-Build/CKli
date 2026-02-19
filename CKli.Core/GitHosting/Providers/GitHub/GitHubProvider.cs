using CK.Core;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitHub hosting provider implementation.
/// Supports https://github.com and GitHub Enterprise instances.
/// </summary>
public sealed partial class GitHubProvider : HttpGitHostingProvider
{
    GitHubProvider( string baseUrl, IGitRepositoryAccessKey gitKey, Uri baseApiUrl )
        : base( baseUrl, gitKey, baseApiUrl, alwaysUseAuthentication: true )
    {
    }

    /// <summary>
    /// Constructor for the cloud https://github.com (internal only).
    /// </summary>
    /// <param name="gitKey">The git key to use.</param>
    internal GitHubProvider( IGitRepositoryAccessKey gitKey )
        : this( "https://github.com", gitKey, new Uri( "https://api.github.com" ) )
    {
    }

    /// <summary>
    /// Constructor for a GitHub server.
    /// </summary>
    /// <param name="baseUrl">The <see cref="HttpGitHostingProvider.BaseUrl"/>.</param>
    /// <param name="gitKey">The git key to use.</param>
    /// <param name="authority">
    /// The authority: currently UriComponents.UserInfo | UriComponents.Host | UriComponents.Port
    /// but this may change.
    /// </param>
    public GitHubProvider( string baseUrl, IGitRepositoryAccessKey gitKey, string authority )
        : this( baseUrl, gitKey, new Uri( $"https://{authority}/api/v3" ) )
    {
    }

    protected internal override NormalizedPath GetRepositoryPathFromUrl( IActivityMonitor monitor, GitRepositoryKey key )
    {
        Throw.DebugAssert( key.OriginUrl.ToString().StartsWith( BaseUrl, StringComparison.OrdinalIgnoreCase ) );
        // No intermediate "folder" exist for GitHub: the repository is the last part of the url path.
        return key.RepositoryName;
    }


    protected override void DefaultConfigure( HttpClient client )
    {
        base.DefaultConfigure( client );
        client.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/vnd.github+json" ) );
        client.DefaultRequestHeaders.Add( "X-GitHub-Api-Version", "2022-11-28" );
    }

    protected override NormalizedPath ValidateRepoPath( IActivityMonitor monitor, NormalizedPath repoPath )
    {
        if( repoPath.Parts.Count != 2 )
        {
            monitor.Error( $"Invalid GitHub repository path '{repoPath}'. Must be '<owner>/<name>'." );
            return default;
        }
        return repoPath;
    }

    protected override bool IsSuccessfulResponse( HttpResponseMessage response )
    {
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound;
    }

    protected override async Task<HostedRepositoryInfo?> GetRepositoryInfoAsync( IActivityMonitor monitor,
                                                                                 HttpClient client,
                                                                                 NormalizedPath repoPath,
                                                                                 bool mustExist,
                                                                                 CancellationToken cancellation = default )
    {
        using var response = await client.GetAsync( $"repos/{repoPath}", cancellation ).ConfigureAwait( false );
        if( response.StatusCode == HttpStatusCode.NotFound )
        {
            return mustExist
                    ? LogErrorNotFound( monitor, repoPath )
                    : new HostedRepositoryInfo() { RepoPath = default };
        }
        if( !response.IsSuccessStatusCode )
        {
            await LogResponseAsync( monitor, response, LogLevel.Error );
            return null;
        }
        return await ReadHostedRepositoryInfoAsync( monitor, response, cancellation ).ConfigureAwait( false );
    }

    protected override async Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                                HttpClient client,
                                                                                NormalizedPath repoPath,
                                                                                bool? isPrivate = null,
                                                                                CancellationToken cancellation = default )
    {
        var request = new GitHubCreateRepoRequest
        {
            Name = repoPath.LastPart,
            Description = "Created by CKli.",
            Private = isPrivate ?? !IsDefaultPublic,
        };

        // Determine if we're creating in an org or for the authenticated user
        // If owner matches authenticated user, use user/repos
        // Otherwise, use /orgs/{org}/repos
        var url = $"orgs/{repoPath.FirstPart}/repos";
        var response = await client.PostAsJsonAsync( url, request, cancellation );

        // If 404 on org endpoint, the owner might be a user - try user repos endpoint.
        if( response.StatusCode == System.Net.HttpStatusCode.NotFound )
        {
            // For user repos, we use user/repos endpoint
            // This requires the owner to be the authenticated user
            url = "user/repos";
            response = await client.PostAsJsonAsync( url, request, cancellation );
        }
        return await ReadHostedRepositoryInfoAsync( monitor, response, cancellation ).ConfigureAwait( false );
    }

    public override bool CanArchiveRepository => true;

    protected override async Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor,
                                                                HttpClient client,
                                                                NormalizedPath repoPath,
                                                                bool archive,
                                                                CancellationToken cancellation )
    {
        var update = new GitHubUpdateArchiveRequest { Archived = archive };
        var response = await client.PatchAsJsonAsync( $"repos/{repoPath}", update, cancellation );
        return response.IsSuccessStatusCode;
    }

    protected override async Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor,
                                                               HttpClient client,
                                                               NormalizedPath repoPath,
                                                               CancellationToken cancellation = default )
    {
        var response = await client.DeleteAsync( $"repos/{repoPath}", cancellation );
        return response.IsSuccessStatusCode;
    }

    static async Task<GitHubRepositoryInfo?> ReadGitHubRepositoryInfoAsync( IActivityMonitor monitor,
                                                                        HttpResponseMessage response,
                                                                        CancellationToken cancellation )
    {
        Throw.DebugAssert( response.IsSuccessStatusCode );
        var r = await response.Content.ReadFromJsonAsync<GitHubRepositoryInfo>( JsonSerializerOptions.Default, cancellation ).ConfigureAwait( false );
        if( r == null )
        {
            monitor.Error( $"Empty response from '{response.RequestMessage?.RequestUri}'." );
            return null;
        }
        return r;
    }

    static async Task<HostedRepositoryInfo?> ReadHostedRepositoryInfoAsync( IActivityMonitor monitor,
                                                                            HttpResponseMessage response,
                                                                            CancellationToken cancellation )
    {
        var gitHubInfo = await ReadGitHubRepositoryInfoAsync( monitor, response, cancellation );
        return gitHubInfo != null
                ? new HostedRepositoryInfo()
                {
                    RepoPath = new NormalizedPath( gitHubInfo.FullName ),
                    Description = gitHubInfo.Description,
                    IsPrivate = gitHubInfo.Private,
                    IsArchived = gitHubInfo.Archived,
                    CloneUrl = gitHubInfo.CloneUrl,
                    WebUrl = gitHubInfo.HtmlUrl,
                    CreatedAt = gitHubInfo.CreatedAt,
                    UpdatedAt = gitHubInfo.UpdatedAt
                }
                : null;
    }

    // Support (TODO) with the HttpRetryState (or a specialization of it).
    // The HttpRetryState should be registered in HttpRequestMessage.Options.
    // 
    // From: https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api?apiVersion=2022-11-28#exceeding-the-rate-limit
    //
    // If you exceed your primary rate limit, you will receive a 403 or 429 response, and the x-ratelimit-remaining header will be 0.
    // You should not retry your request until after the time specified by the x-ratelimit-reset header.
    //
    // If you exceed a secondary rate limit, you will receive a 403 or 429 response and an error message that indicates that you
    // exceeded a secondary rate limit.
    // If the retry-after response header is present, you should not retry your request until after that many seconds has elapsed.
    // If the x-ratelimit-remaining header is 0, you should not retry your request until after the time, in UTC epoch seconds, specified
    // by the x-ratelimit-reset header.
    //
    // Otherwise, wait for at least one minute before retrying.
    // If your request continues to fail due to a secondary rate limit,
    // wait for an exponentially increasing amount of time between retries,
    // and throw an error after a specific number of retries.
    //
    //
    //if( response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests )
    //{
    //    TimeSpan minFromRateLimit = TimeSpan.Zero;
    //    if( response.Headers.TryGetValues( "x-ratelimit-remaining", out var v )
    //        && v.FirstOrDefault() == "0"
    //        && response.Headers.TryGetValues( "x-ratelimit-reset", out v ) )
    //    {
    //        if( int.TryParse( v.FirstOrDefault(), out int minSecondsToWait ) && minSecondsToWait > 0 )
    //        {
    //            minFromRateLimit = TimeSpan.FromSeconds( minSecondsToWait );
    //        }
    //    }
    //
    //    protected override Task<TimeSpan?> OnFailedResponseAsync( IActivityMonitor monitor, HttpRequestMessage request, HttpResponseMessage response )
    //    {
    //        return base.OnFailedResponseAsync( monitor, request, response );
    //    }

}

