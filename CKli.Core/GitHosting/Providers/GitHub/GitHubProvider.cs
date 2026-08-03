using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core.GitHosting.Providers;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

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
    /// <param name="baseUrl">The <see cref="GitHostingProvider.BaseUrl"/>.</param>
    /// <param name="gitKey">The git key to use.</param>
    /// <param name="authority">
    /// The authority: currently UriComponents.UserInfo | UriComponents.Host | UriComponents.Port
    /// but this may change.
    /// </param>
    public GitHubProvider( string baseUrl, IGitRepositoryAccessKey gitKey, string authority )
        : this( baseUrl, gitKey, new Uri( $"https://{authority}/api/v3" ) )
    {
    }


    /// <inheritdoc />
    /// <remarks>
    /// Sets the "Accept" header to "application/vnd.github+json" to opt in to the latest GitHub API version and avoid compatibility issues.
    /// </remarks>
    protected override void DefaultConfigure( HttpClient client )
    {
        base.DefaultConfigure( client );
        client.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/vnd.github+json" ) );
        client.DefaultRequestHeaders.Add( "X-GitHub-Api-Version", "2022-11-28" );
    }

    /// <inheritdoc />
    protected internal override NormalizedPath GetRepositoryPathFromUrl( IActivityMonitor monitor, GitRepositoryKey key )
    {
        var sUrl = key.OriginUrl.ToString();
        Throw.DebugAssert( sUrl.StartsWith( BaseUrl + '/', StringComparison.OrdinalIgnoreCase ) );
        // Returns the "<owner>/<repo>" string.
        return sUrl.Substring( BaseUrl.Length + 1 );
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
            return null;
        }
        return await ReadHostedRepositoryInfoAsync( monitor, response, cancellation ).ConfigureAwait( false );
    }

    protected override async Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                                HttpClient client,
                                                                                NormalizedPath repoPath,
                                                                                bool isPrivate,
                                                                                string defaultBranchName,
                                                                                CancellationToken cancellation )
    {
        var request = new GitHubCreateRepoRequest
        {
            Name = repoPath.LastPart,
            Description = "Created by CKli.",
            Private = isPrivate,
            DefaultBranch = defaultBranchName,
        };

        // Determine if we're creating inside an organization or for the authenticated user
        // If owner matches authenticated user, use user/repos
        // Otherwise, use /orgs/{org}/repos
        var url = $"orgs/{repoPath.FirstPart}/repos";
        var response = await client.PostAsJsonAsync( url, request, cancellation );

        // If 404 on organization endpoint, the owner might be a user - try user repos endpoint.
        if( response.StatusCode == System.Net.HttpStatusCode.NotFound )
        {
            // For user repos, we use user/repos endpoint
            // This requires the owner to be the authenticated user
            url = "user/repos";
            response = await client.PostAsJsonAsync( url, request, cancellation );
        }
        if( !response.IsSuccessStatusCode )
        {
            return null;
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
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound;
    }

    protected override async Task<string?> CreateDraftReleaseAsync( IActivityMonitor monitor,
                                                                    HttpClient client,
                                                                    NormalizedPath repoPath,
                                                                    string versionedTag,
                                                                    CancellationToken cancellation )
    {
        var request = new GitHubCreateReleaseRequest
        {
            TagName = versionedTag,
            Name = versionedTag,
            Draft = true,
            Prerelease = versionedTag.Contains( '-' ),
            GenerateReleaseNotes = true
        };
        using var response = await client.PostAsJsonAsync( $"repos/{repoPath}/releases", request, cancellation ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode )
        {
            if( response.StatusCode == HttpStatusCode.NotFound )
            {
                await LogResponseAsync( monitor, response, LogLevel.Error ).ConfigureAwait( false );
            }
            return null;
        }
        var releaseInfo = await response.Content.ReadFromJsonAsync<GitHubReleaseInfo>( JsonSerializerOptions.Default, cancellation ).ConfigureAwait( false );
        if( releaseInfo == null )
        {
            monitor.Error( $"Empty response from '{response.RequestMessage?.RequestUri}'." );
            return null;
        }
        // Strip the URI template suffix "{?name,label}" from the upload_url.
        var uploadUrlBase = releaseInfo.UploadUrl;
        var templateIdx = uploadUrlBase.IndexOf( '{' );
        if( templateIdx > 0 ) uploadUrlBase = uploadUrlBase[..templateIdx];
        // Encode release id and upload URL base together so AddReleaseAssetAsync can use both.
        return $"{releaseInfo.Id}|{uploadUrlBase}";
    }

    protected override async Task<bool> AddReleaseAssetAsync( IActivityMonitor monitor,
                                                              HttpClient client,
                                                              NormalizedPath repoPath,
                                                              string releaseIdentifier,
                                                              NormalizedPath filePath,
                                                              string fileName,
                                                              CancellationToken cancellation )
    {
        var pipeIdx = releaseIdentifier.IndexOf( '|' );
        if( pipeIdx < 0 )
        {
            monitor.Error( $"Invalid release identifier '{releaseIdentifier}': expected '<id>|<uploadUrlBase>'." );
            return false;
        }
        var uploadUrlBase = releaseIdentifier[(pipeIdx + 1)..];
        var uploadUrl = $"{uploadUrlBase}?name={Uri.EscapeDataString( fileName )}";
        await using var fileStream = File.OpenRead( filePath );
        using var content = new StreamContent( fileStream );
        content.Headers.ContentType = new MediaTypeHeaderValue( "application/octet-stream" );
        using var response = await client.PostAsync( uploadUrl, content, cancellation ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode )
        {
            if( response.StatusCode == HttpStatusCode.NotFound )
            {
                await LogResponseAsync( monitor, response, LogLevel.Error ).ConfigureAwait( false );
            }
            return false;
        }
        return true;
    }

    protected override async Task<bool> FinalizeReleaseAsync( IActivityMonitor monitor,
                                                              HttpClient client,
                                                              NormalizedPath repoPath,
                                                              string releaseIdentifier,
                                                              CancellationToken cancellation )
    {
        var pipeIdx = releaseIdentifier.IndexOf( '|' );
        var releaseId = pipeIdx >= 0 ? releaseIdentifier[..pipeIdx] : releaseIdentifier;
        var update = new GitHubPublishReleaseRequest { Draft = false };
        using var response = await client.PatchAsJsonAsync( $"repos/{repoPath}/releases/{releaseId}", update, cancellation ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode )
        {
            if( response.StatusCode == HttpStatusCode.NotFound )
            {
                await LogResponseAsync( monitor, response, LogLevel.Error ).ConfigureAwait( false );
            }
            return false;
        }
        return true;
    }

    protected override async Task<List<PublishedReleaseInfo>?> GetReleaseListAsync( IActivityMonitor monitor,
                                                                                    HttpClient client,
                                                                                    NormalizedPath repoPath,
                                                                                    int pageNumber,
                                                                                    int countPerPage,
                                                                                    CancellationToken cancellation )
    {
        List<PublishedReleaseInfo>? result = null;
        var url = $"repos/{repoPath}/releases?page={pageNumber}&per_page={countPerPage}";
        using var response = await client.GetAsync( url, cancellation ).ConfigureAwait( false );
        if( response.IsSuccessStatusCode )
        {
            var releases = await response.Content.ReadFromJsonAsync<JsonElement[]>( JsonSerializerOptions.Default, cancellation ).ConfigureAwait( false );
            if( releases != null )
            {
                result = new List<PublishedReleaseInfo>( releases.Length );
                foreach( var r in releases )
                {
                    try
                    {
                        var tag = r.TryGetProperty( "tag_name", out var tp ) && tp.ValueKind == JsonValueKind.String
                                    ? tp.GetString() ?? ""
                                    : "";
                        bool draft = r.TryGetProperty( "draft", out var pd ) && pd.ValueKind == JsonValueKind.True;
                        DateTime? createdAt = null;
                        if( r.TryGetProperty( "created_at", out var cp ) && cp.ValueKind != JsonValueKind.Null )
                        {
                            // JsonElement.GetDateTime will parse RFC3339 timestamps.
                            createdAt = cp.GetDateTime();
                        }
                        string description = r.TryGetProperty( "body", out var bd ) && bd.ValueKind != JsonValueKind.Null
                                                ? bd.GetString() ?? ""
                                                : "";
                        string releaseId;
                        if( r.TryGetProperty( "id", out var idp ) )
                        {
                            // Prefer a string representation of the id.
                            releaseId = idp.ValueKind == JsonValueKind.Number ? idp.GetInt64().ToString() : idp.ToString();
                        }
                        else
                        {
                            releaseId = tag;
                        }
                        var assets = new List<string>();
                        if( r.TryGetProperty( "assets", out var ap ) && ap.ValueKind == JsonValueKind.Array )
                        {
                            foreach( var a in ap.EnumerateArray() )
                            {
                                if( a.TryGetProperty( "name", out var an ) && an.ValueKind == JsonValueKind.String )
                                {
                                    var n = an.GetString();
                                    if( n != null ) assets.Add( n );
                                }
                            }
                        }

                        var info = PublishedReleaseInfo.Create( monitor, tag, !draft, createdAt, description, releaseId, assets );
                        if( info != null )
                        {
                            result?.Add( info );
                        }
                    }
                    catch( Exception ex )
                    {
                        monitor.Error( $"While parsing '{r}'.", ex );
                        result = null;
                    }
                }
            }
        }
        if( result == null )
        {
            // Log error and return null to indicate failure.
            await LogResponseAsync( monitor, response, LogLevel.Error ).ConfigureAwait( false );
        }
        return result;
    }

    protected override async Task<(bool Success, PublishedReleaseInfo? Info)> GetReleaseAsync( IActivityMonitor monitor,
                                                                                               HttpClient client,
                                                                                               NormalizedPath repoPath,
                                                                                               string releaseId,
                                                                                               CancellationToken cancellation )
    {
        // Support release identifiers that may be the encoded string produced by CreateDraftReleaseAsync ("<id>|<uploadUrlBase>").
        var pipeIdx = releaseId.IndexOf( '|' );
        var idToUse = pipeIdx >= 0 ? releaseId[..pipeIdx] : releaseId;

        using var response = await client.GetAsync( $"repos/{repoPath}/releases/{idToUse}", cancellation ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound )
        {
            await LogResponseAsync( monitor, response, LogLevel.Error ).ConfigureAwait( false );
            return (false, null);
        }
        if( response.StatusCode == HttpStatusCode.NotFound )
        {
            return (true, null);
        }
        Throw.DebugAssert( response.IsSuccessStatusCode );

        try
        {
            var r = await response.Content.ReadFromJsonAsync<JsonElement>( JsonSerializerOptions.Default, cancellation ).ConfigureAwait( false );

            var tag = r.TryGetProperty( "tag_name", out var tp ) && tp.ValueKind == JsonValueKind.String
                        ? tp.GetString() ?? ""
                        : "";
            bool draft = r.TryGetProperty( "draft", out var pd ) && pd.ValueKind == JsonValueKind.True;
            DateTime? createdAt = null;
            if( r.TryGetProperty( "created_at", out var cp ) && cp.ValueKind != JsonValueKind.Null )
            {
                createdAt = cp.GetDateTime();
            }
            string description = r.TryGetProperty( "body", out var bd ) && bd.ValueKind != JsonValueKind.Null
                                    ? bd.GetString() ?? ""
                                    : "";
            string resolvedReleaseId;
            if( r.TryGetProperty( "id", out var idp ) )
            {
                resolvedReleaseId = idp.ValueKind == JsonValueKind.Number ? idp.GetInt64().ToString() : idp.ToString();
            }
            else
            {
                resolvedReleaseId = tag;
            }
            var assets = new List<string>();
            if( r.TryGetProperty( "assets", out var ap ) && ap.ValueKind == JsonValueKind.Array )
            {
                foreach( var a in ap.EnumerateArray() )
                {
                    if( a.TryGetProperty( "name", out var an ) && an.ValueKind == JsonValueKind.String )
                    {
                        var n = an.GetString();
                        if( n != null ) assets.Add( n );
                    }
                }
            }

            return (true, PublishedReleaseInfo.Create( monitor, tag, !draft, createdAt, description, resolvedReleaseId, assets ));
        }
        catch( Exception ex )
        {
            monitor.Error( $"While parsing release response.", ex );
            return (false,null);
        }
    }

    protected override async Task<bool> DeleteReleaseAsync( IActivityMonitor monitor,
                                                            HttpClient client,
                                                            NormalizedPath repoPath,
                                                            string releaseId,
                                                            CancellationToken cancellation )
    {
        // releaseId may be encoded as "<id>|<uploadUrlBase>".
        var pipeIdx = releaseId.IndexOf( '|' );
        var idToUse = pipeIdx >= 0 ? releaseId[..pipeIdx] : releaseId;

        using var response = await client.DeleteAsync( $"repos/{repoPath}/releases/{idToUse}", cancellation ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound )
        {
            await LogResponseAsync( monitor, response, LogLevel.Error );
            return false;
        }
        return true;
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

