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
/// Gitea hosting provider implementation.
/// Gitea is self-hosted only - there is no official cloud instance.
/// <para>
/// This provider is almost a clone of the <see cref="GitHubProvider"/>.
/// </para>
/// </summary>
public sealed partial class GiteaProvider : HttpGitHostingProvider
{
    public GiteaProvider( string baseUrl, IGitRepositoryAccessKey gitKey, string authority )
        : base( baseUrl, gitKey, new Uri( $"https://{authority}/api/v3/" ), alwaysUseAuthentication: false )
    {
        HasDefaultBranch = true;
    }

    protected override void DefaultConfigure( HttpClient client )
    {
        base.DefaultConfigure( client );
        client.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json" ) );
    }

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
            monitor.Error( $"Invalid Gitea repository path '{repoPath}'. Must be '<owner>/<name>'." );
            return default;
        }
        return repoPath;
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
            return await LogFailedAsync<HostedRepositoryInfo>( monitor, response ).ConfigureAwait( false );
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
        var request = new GiteaCreateRepoRequest
        {
            Name = repoPath.LastPart,
            Description = "Created by CKli.",
            Private = isPrivate,
            DefaultBranch = defaultBranchName
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
        if( !response.IsSuccessStatusCode )
        {
            return await LogFailedAsync<HostedRepositoryInfo>( monitor, response ).ConfigureAwait( false );
        }
        return await ReadHostedRepositoryInfoAsync( monitor, response, cancellation ).ConfigureAwait( false );
    }

    protected override async Task<bool> SetDefaultBranchAsync( IActivityMonitor monitor,
                                                               HttpClient client,
                                                               NormalizedPath repoPath,
                                                               string branchName,
                                                               CancellationToken cancellation )
    {
        var update = new GiteaUpdateDefaultBranchRequest { DefaultBranch = branchName };
        using var response = await client.PatchAsJsonAsync( $"repos/{repoPath}", update, cancellation ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode )
        {
            // The branch must exist: Gitea answers a 422 when it doesn't.
            return await LogFailedAsync( monitor, response ).ConfigureAwait( false );
        }
        return true;
    }

    public override bool CanArchiveRepository => true;

    protected override async Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor,
                                                                HttpClient client,
                                                                NormalizedPath repoPath,
                                                                bool archive,
                                                                CancellationToken cancellation )
    {
        var update = new GiteaUpdateArchiveRequest { Archived = archive };
        using var response = await client.PatchAsJsonAsync( $"repos/{repoPath}", update, cancellation );
        if( !response.IsSuccessStatusCode )
        {
            return await LogFailedAsync( monitor, response ).ConfigureAwait( false );
        }
        return true;
    }

    protected override async Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor,
                                                               HttpClient client,
                                                               NormalizedPath repoPath,
                                                               CancellationToken cancellation = default )
    {
        using var response = await client.DeleteAsync( $"repos/{repoPath}", cancellation );
        // Deleting a repository that doesn't exist is a no-op success.
        if( !response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound )
        {
            return await LogFailedAsync( monitor, response ).ConfigureAwait( false );
        }
        return true;
    }

    protected override async Task<string?> CreateDraftReleaseAsync( IActivityMonitor monitor,
                                                                    HttpClient client,
                                                                    NormalizedPath repoPath,
                                                                    string versionedTag,
                                                                    CancellationToken cancellation )
    {
        var request = new GiteaCreateReleaseRequest
        {
            TagName = versionedTag,
            Name = versionedTag,
            Draft = true,
            Prerelease = versionedTag.Contains( '-' )
        };
        using var response = await client.PostAsJsonAsync( $"repos/{repoPath}/releases", request, cancellation ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode )
        {
            return await LogFailedAsync<string>( monitor, response ).ConfigureAwait( false );
        }
        var releaseInfo = await response.Content.ReadFromJsonAsync<GiteaReleaseInfo>( JsonSerializerOptions.Default, cancellation ).ConfigureAwait( false );
        if( releaseInfo == null )
        {
            monitor.Error( $"Empty response from '{response.RequestMessage?.RequestUri}'." );
            return null;
        }
        return releaseInfo.Id.ToString();
    }

    protected override async Task<List<PublishedReleaseInfo>?> GetReleaseListAsync( IActivityMonitor monitor,
                                                                                    HttpClient client,
                                                                                    NormalizedPath repoPath,
                                                                                    int pageNumber,
                                                                                    int countPerPage,
                                                                                    CancellationToken cancellation )
    {
        List<PublishedReleaseInfo>? result = null;
        var url = $"repos/{repoPath}/releases?page={pageNumber}&limit={countPerPage}";
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
                        var tag = r.GetProperty( "tag_name" ).GetString() ?? "";
                        DateTime? createdAt = null;
                        if( r.TryGetProperty( "created_at", out var cp ) && cp.ValueKind != JsonValueKind.Null ) createdAt = cp.GetDateTime();
                        string description = r.TryGetProperty( "description", out var d ) && d.ValueKind != JsonValueKind.Null ? d.GetString() ?? "" : "";
                        var assets = new List<string>();
                        if( r.TryGetProperty( "assets", out var ap ) && ap.ValueKind == JsonValueKind.Array )
                        {
                            foreach( var a in ap.EnumerateArray() )
                            {
                                if( a.TryGetProperty( "name", out var n ) && n.ValueKind == JsonValueKind.String )
                                {
                                    assets.Add( n.GetString()! );
                                }
                            }
                        }
                        var info = PublishedReleaseInfo.Create( monitor, tag, isPublished: false, createdAt, description, tag, assets );
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

    protected override async Task<bool> AddReleaseAssetAsync( IActivityMonitor monitor,
                                                              HttpClient client,
                                                              NormalizedPath repoPath,
                                                              string releaseIdentifier,
                                                              NormalizedPath filePath,
                                                              string fileName,
                                                              CancellationToken cancellation )
    {
        // Gitea expects multipart/form-data with field "attachment".
        await using var fileStream = File.OpenRead( filePath );
        using var formContent = new MultipartFormDataContent();
        var fileContent = new StreamContent( fileStream );
        fileContent.Headers.ContentType = new MediaTypeHeaderValue( "application/octet-stream" );
        formContent.Add( fileContent, "attachment", fileName );
        var uploadUrl = $"repos/{repoPath}/releases/{releaseIdentifier}/assets?name={Uri.EscapeDataString( fileName )}";
        using var response = await client.PostAsync( uploadUrl, formContent, cancellation ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode )
        {
            return await LogFailedAsync( monitor, response ).ConfigureAwait( false );
        }
        return true;
    }

    protected override async Task<bool> FinalizeReleaseAsync( IActivityMonitor monitor,
                                                              HttpClient client,
                                                              NormalizedPath repoPath,
                                                              string releaseIdentifier,
                                                              CancellationToken cancellation )
    {
        var update = new GiteaPublishReleaseRequest { Draft = false };
        using var response = await client.PatchAsJsonAsync( $"repos/{repoPath}/releases/{releaseIdentifier}", update, cancellation ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode )
        {
            return await LogFailedAsync( monitor, response ).ConfigureAwait( false );
        }
        return true;
    }

    protected override async Task<(bool Success, PublishedReleaseInfo? Info)> GetReleaseAsync( IActivityMonitor monitor,
                                                                                               HttpClient client,
                                                                                               NormalizedPath repoPath,
                                                                                               string releaseId,
                                                                                               CancellationToken cancellation )
    {
        using var response = await client.GetAsync( $"repos/{repoPath}/releases/{releaseId}", cancellation ).ConfigureAwait( false );
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
            var tag = r.TryGetProperty( "tag_name", out var tp ) && tp.ValueKind == JsonValueKind.String ? tp.GetString() ?? "" : "";
            DateTime? createdAt = null;
            if( r.TryGetProperty( "created_at", out var cp ) && cp.ValueKind != JsonValueKind.Null ) createdAt = cp.GetDateTime();
            string description = r.TryGetProperty( "description", out var d ) && d.ValueKind != JsonValueKind.Null ? d.GetString() ?? "" : "";
            var assets = new List<string>();
            if( r.TryGetProperty( "assets", out var ap ) && ap.ValueKind == JsonValueKind.Array )
            {
                foreach( var a in ap.EnumerateArray() )
                {
                    if( a.TryGetProperty( "name", out var n ) && n.ValueKind == JsonValueKind.String )
                    {
                        assets.Add( n.GetString()! );
                    }
                }
            }
            // Gitea release id is the numeric id, but we use the provided releaseId as ReleaseId.
            return (true, PublishedReleaseInfo.Create( monitor, tag, isPublished: true, createdAt, description, releaseId, assets ));
        }
        catch( Exception ex )
        {
            monitor.Error( $"While parsing release response.", ex );
            return (false, null);
        }
    }

    protected override async Task<bool> DeleteReleaseAsync( IActivityMonitor monitor,
                                                            HttpClient client,
                                                            NormalizedPath repoPath,
                                                            string releaseId,
                                                            CancellationToken cancellation )
    {
        using var response = await client.DeleteAsync( $"repos/{repoPath}/releases/{releaseId}", cancellation ).ConfigureAwait( false );
        // Deleting a release that doesn't exist is a no-op success.
        if( !response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound )
        {
            return await LogFailedAsync( monitor, response ).ConfigureAwait( false );
        }
        return true;
    }

    static async Task<GiteaRepositoryInfo?> ReadGiteaRepositoryInfoAsync( IActivityMonitor monitor,
                                                                        HttpResponseMessage response,
                                                                        CancellationToken cancellation )
    {
        Throw.DebugAssert( response.IsSuccessStatusCode );
        var r = await response.Content.ReadFromJsonAsync<GiteaRepositoryInfo>( JsonSerializerOptions.Default, cancellation ).ConfigureAwait( false );
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
        var giteaInfo = await ReadGiteaRepositoryInfoAsync( monitor, response, cancellation );
        return giteaInfo != null
                ? new HostedRepositoryInfo()
                {
                    RepoPath = new NormalizedPath( giteaInfo.FullName ),
                    Description = giteaInfo.Description,
                    IsPrivate = giteaInfo.Private,
                    IsArchived = giteaInfo.Archived,
                    DefaultBranch = giteaInfo.DefaultBranch,
                    CloneUrl = giteaInfo.CloneUrl,
                    WebUrl = giteaInfo.HtmlUrl,
                    CreatedAt = giteaInfo.CreatedAt,
                    UpdatedAt = giteaInfo.UpdatedAt
                }
                : null;
    }

}

