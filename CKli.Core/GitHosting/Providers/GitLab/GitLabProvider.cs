using CK.Core;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace CKli.Core.GitHosting.Providers;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

/// <summary>
/// GitLab hosting provider implementation.
/// Supports https://gitlab.com and on-premises instances.
/// </summary>
public sealed partial class GitLabProvider : HttpGitHostingProvider
{
    GitLabProvider( string baseUrl, IGitRepositoryAccessKey gitKey, Uri baseApiUrl )
        : base( baseUrl, gitKey, baseApiUrl, alwaysUseAuthentication: true )
    {
    }

    /// <summary>
    /// Constructor for the cloud https://gitlab.com. (internal only)
    /// </summary>
    /// <param name="gitKey">The git key to use.</param>
    internal GitLabProvider( IGitRepositoryAccessKey gitKey )
        : this( "https://gitlab.com", gitKey, new Uri( "https://gitlab.com/api/v4" ) )
    {
    }

    /// <summary>
    /// Constructor for a GitHub server.
    /// </summary>
    /// <param name="baseUrl">The <see cref="HttpGitHostingProvider.BaseApiUrl"/>.</param>
    /// <param name="gitKey">The git key to use.</param>
    /// <param name="authority">The authority.</param>
    public GitLabProvider( string baseUrl, IGitRepositoryAccessKey gitKey, string authority )
        : this( baseUrl, gitKey, new Uri( $"https://{authority}/api/v4/" ) )
    {
    }

    protected override void DefaultConfigure( HttpClient client )
    {
        base.DefaultConfigure( client );
        client.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json" ) );
    }

    protected internal override NormalizedPath GetRepositoryPathFromUrl( IActivityMonitor monitor, GitRepositoryKey key )
    {
        Throw.DebugAssert( key.OriginUrl.ToString().StartsWith( BaseUrl, StringComparison.OrdinalIgnoreCase ) );
        // GitLab can have group/subgroup/repo structure, so we take the path part of the url and removes
        // the first part that is the "owner" of the repository.
        //
        // !! NOT SURE HERE. TO BE TESTED.
        //
        NormalizedPath path = key.OriginUrl.AbsolutePath;
        return path.RemoveFirstPart();
    }

    protected override NormalizedPath ValidateRepoPath( IActivityMonitor monitor, NormalizedPath repoPath )
    {
        if( repoPath.Parts.Count < 2 )
        {
            monitor.Error( $"Invalid GitHub repository path '{repoPath}'. Must be '<owner>/../<name>'." );
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
        // GitLab uses URL-encoded project path: group%2Fsubgroup%2Frepo.
        var projectPath = HttpUtility.UrlEncode( repoPath );
        using var response = await client.GetAsync( $"projects/{projectPath}", cancellation ).ConfigureAwait( false );
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
                                                                                bool isPrivate,
                                                                                CancellationToken cancellation = default )
    {

        // First, try to get the namespace ID for the owner.
        long? namespaceId = await GetNamespaceIdAsync( client, repoPath, cancellation );

        var request = new GitLabCreateProjectRequest
        {
            Name = repoPath.LastPart,
            Path = repoPath,
            Description = "Created by CKli.",
            Visibility = isPrivate ? "private" : "public",
            NamespaceId = namespaceId
        };

        var response = await client.PostAsJsonAsync( "projects", request, cancellation );

        return await ReadHostedRepositoryInfoAsync( monitor, response, cancellation ).ConfigureAwait( false );
    }

    /// <summary>
    /// Gets the namespace ID for a group or user path.
    /// </summary>
    async Task<long?> GetNamespaceIdAsync( HttpClient client, NormalizedPath namespacePath, CancellationToken cancellation )
    {
        var encodedPath = HttpUtility.UrlEncode( namespacePath );
        var response = await client.GetAsync( $"namespaces/{encodedPath}", cancellation );
        if( response.IsSuccessStatusCode )
        {
            var ns = await response.Content.ReadFromJsonAsync<GitLabNamespace>( cancellation );
            return ns?.Id;
        }
        return null;
    }

    public override bool CanArchiveRepository => true;

    protected override async Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor,
                                                                HttpClient client,
                                                                NormalizedPath repoPath,
                                                                bool archive,
                                                                CancellationToken cancellation )
    {
        var projectPath = HttpUtility.UrlEncode( repoPath );
        var response = await client.PostAsync( $"projects/{projectPath}/archive", null, cancellation );
        return response.IsSuccessStatusCode;
    }

    protected override async Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor,
                                                               HttpClient client,
                                                               NormalizedPath repoPath,
                                                               CancellationToken cancellation = default )
    {
        var projectPath = HttpUtility.UrlEncode( repoPath );
        var response = await client.DeleteAsync( $"projects/{projectPath}", cancellation );
        return response.IsSuccessStatusCode;
    }

    protected override async Task<string?> CreateDraftReleaseAsync( IActivityMonitor monitor,
                                                                    HttpClient client,
                                                                    NormalizedPath repoPath,
                                                                    string versionedTag,
                                                                    CancellationToken cancellation )
    {
        // GitLab has no draft release concept: the release is published immediately.
        var request = new GitLabCreateReleaseRequest
        {
            TagName = versionedTag,
            Name = versionedTag
        };
        var projectPath = HttpUtility.UrlEncode( repoPath );
        using var response = await client.PostAsJsonAsync( $"projects/{projectPath}/releases", request, cancellation ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode )
        {
            await LogResponseAsync( monitor, response, LogLevel.Error ).ConfigureAwait( false );
            return null;
        }
        var releaseInfo = await response.Content.ReadFromJsonAsync<GitLabReleaseInfo>( JsonSerializerOptions.Default, cancellation ).ConfigureAwait( false );
        if( releaseInfo == null || string.IsNullOrEmpty( releaseInfo.TagName ) )
        {
            monitor.Error( $"Empty response from '{response.RequestMessage?.RequestUri}'." );
            return null;
        }
        return releaseInfo.TagName;
    }

    protected override async Task<bool> AddReleaseAssetAsync( IActivityMonitor monitor,
                                                              HttpClient client,
                                                              NormalizedPath repoPath,
                                                              string releaseIdentifier,
                                                              NormalizedPath filePath,
                                                              string fileName,
                                                              CancellationToken cancellation )
    {
        // Step 1: upload file to the Generic Package Registry.
        // The package name and version mirror the release tag for discoverability.
        var projectPath = HttpUtility.UrlEncode( repoPath );
        var tagName = releaseIdentifier;
        var packagePath = $"projects/{projectPath}/packages/generic/{Uri.EscapeDataString( tagName )}/{Uri.EscapeDataString( tagName )}/{Uri.EscapeDataString( fileName )}";
        await using var fileStream = File.OpenRead( filePath );
        using var fileContent = new StreamContent( fileStream );
        fileContent.Headers.ContentType = new MediaTypeHeaderValue( "application/octet-stream" );
        using var uploadResponse = await client.PutAsync( packagePath, fileContent, cancellation ).ConfigureAwait( false );
        if( !uploadResponse.IsSuccessStatusCode )
        {
            await LogResponseAsync( monitor, uploadResponse, LogLevel.Error ).ConfigureAwait( false );
            return false;
        }
        // Step 2: create a release asset link pointing to the uploaded package.
        var downloadUrl = $"{BaseApiUrl}/{packagePath}";
        var linkRequest = new GitLabAssetLinkRequest { Name = fileName, Url = downloadUrl };
        var encodedTag = Uri.EscapeDataString( tagName );
        using var linkResponse = await client.PostAsJsonAsync( $"projects/{projectPath}/releases/{encodedTag}/assets/links", linkRequest, cancellation ).ConfigureAwait( false );
        if( !linkResponse.IsSuccessStatusCode )
        {
            await LogResponseAsync( monitor, linkResponse, LogLevel.Error ).ConfigureAwait( false );
            return false;
        }
        return true;
    }

    protected override Task<bool> FinalizeReleaseAsync( IActivityMonitor monitor,
                                                        HttpClient client,
                                                        NormalizedPath repoPath,
                                                        string releaseIdentifier,
                                                        CancellationToken cancellation )
    {
        // GitLab has no draft release concept: the release is already published.
        return Task.FromResult( true );
    }

    static async Task<GitLabProject?> ReadGitLabProjectAsync( IActivityMonitor monitor,
                                                                     HttpResponseMessage response,
                                                                     CancellationToken cancellation )
    {
        Throw.DebugAssert( response.IsSuccessStatusCode );
        var r = await response.Content.ReadFromJsonAsync<GitLabProject>( JsonSerializerOptions.Default, cancellation ).ConfigureAwait( false );
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
        var gitLabProject = await ReadGitLabProjectAsync( monitor, response, cancellation );
        return gitLabProject != null
                ? new HostedRepositoryInfo()
                {
                    RepoPath = new NormalizedPath( gitLabProject.Path ).AppendPart( gitLabProject.Name ),
                    Description = gitLabProject.Description,
                    IsPrivate = gitLabProject.Visibility == "private",
                    IsArchived = gitLabProject.Archived,
                    CloneUrl = gitLabProject.HttpUrlToRepo,
                    WebUrl = gitLabProject.WebUrl,
                    CreatedAt = gitLabProject.CreatedAt,
                    UpdatedAt = gitLabProject.LastActivityAt
                }
                : null;
    }

}

