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
/// Gitea hosting provider implementation.
/// Gitea is self-hosted only - there is no official cloud instance.
/// <para>
/// This provider is almost a clone of the <see cref="GitHubProvider"/>.
/// </para>
/// </summary>
public sealed partial class GiteaProvider : HttpGitHostingProvider
{
    public GiteaProvider( string baseUrl, IGitRepositoryAccessKey gitKey, string authority )
        : base( baseUrl, gitKey, new Uri( $"https://{authority}/api/v3" ), alwaysUseAuthentication: false )
    {
    }

    protected override void DefaultConfigure( HttpClient client )
    {
        base.DefaultConfigure( client );
        client.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json" ) );
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
        var request = new GiteaCreateRepoRequest
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
        var update = new GiteaUpdateArchiveRequest { Archived = archive };
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
                    CloneUrl = giteaInfo.CloneUrl,
                    WebUrl = giteaInfo.HtmlUrl,
                    CreatedAt = giteaInfo.CreatedAt,
                    UpdatedAt = giteaInfo.UpdatedAt
                }
                : null;
    }

}

