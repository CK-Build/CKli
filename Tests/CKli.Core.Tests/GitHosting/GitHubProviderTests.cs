using System.Net.Http;
using System.Threading.Tasks;
using CK.Core;
using CKli.Core.GitHosting;
using CKli.Core.GitHosting.Providers;
using CKli.Core.Tests.GitHosting.Mocks;
using NUnit.Framework;
using Shouldly;

namespace CKli.Core.Tests.GitHosting;

[TestFixture]
public class GitHubProviderTests
{
    static readonly string SampleRepoJson = """
        {
            "id": 12345,
            "name": "test-repo",
            "full_name": "test-owner/test-repo",
            "description": "A test repository",
            "private": true,
            "archived": false,
            "default_branch": "main",
            "clone_url": "https://github.com/test-owner/test-repo.git",
            "ssh_url": "git@github.com:test-owner/test-repo.git",
            "html_url": "https://github.com/test-owner/test-repo",
            "created_at": "2024-01-01T00:00:00Z",
            "updated_at": "2024-01-02T00:00:00Z",
            "owner": {
                "login": "test-owner",
                "type": "Organization"
            }
        }
        """;

    [Test]
    public void ParseRemoteUrl_HttpsUrl()
    {
        using var provider = new GitHubProvider( "fake-pat" );

        var result = provider.ParseRemoteUrl( "https://github.com/test-owner/test-repo" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "test-owner" );
        result.Value.RepoName.ShouldBe( "test-repo" );
    }

    [Test]
    public void ParseRemoteUrl_HttpsUrl_WithGitExtension()
    {
        using var provider = new GitHubProvider( "fake-pat" );

        var result = provider.ParseRemoteUrl( "https://github.com/test-owner/test-repo.git" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "test-owner" );
        result.Value.RepoName.ShouldBe( "test-repo" );
    }

    [Test]
    public void ParseRemoteUrl_SshUrl()
    {
        using var provider = new GitHubProvider( "fake-pat" );

        var result = provider.ParseRemoteUrl( "git@github.com:test-owner/test-repo.git" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "test-owner" );
        result.Value.RepoName.ShouldBe( "test-repo" );
    }

    [Test]
    public void ParseRemoteUrl_ReturnsNullForOtherHosts()
    {
        using var provider = new GitHubProvider( "fake-pat" );

        var result = provider.ParseRemoteUrl( "https://gitlab.com/owner/repo" );

        result.ShouldBeNull();
    }

    [Test]
    public async Task GetRepositoryInfoAsync_SuccessAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( SampleRepoJson );

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "test-owner", "test-repo" );

        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Owner.ShouldBe( "test-owner" );
        result.Data.Name.ShouldBe( "test-repo" );
        result.Data.Description.ShouldBe( "A test repository" );
        result.Data.IsPrivate.ShouldBeTrue();
        result.Data.DefaultBranch.ShouldBe( "main" );
    }

    [Test]
    public async Task GetRepositoryInfoAsync_NotFoundAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueNotFoundResponse();

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "owner", "non-existent" );

        result.Success.ShouldBeFalse();
        result.IsNotFound.ShouldBeTrue();
    }

    [Test]
    public async Task GetRepositoryInfoAsync_UnauthorizedAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueUnauthorizedResponse();

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "owner", "private-repo" );

        result.Success.ShouldBeFalse();
        result.IsAuthenticationError.ShouldBeTrue();
    }

    [Test]
    public async Task CreateRepositoryAsync_SuccessAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueCreatedResponse( SampleRepoJson );

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var options = new RepositoryCreateOptions
        {
            Owner = "test-owner",
            Name = "test-repo",
            Description = "A test repository",
            IsPrivate = true
        };

        var result = await provider.CreateRepositoryAsync( monitor, options );

        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Name.ShouldBe( "test-repo" );
        mockHandler.Requests.Count.ShouldBe( 1 );
    }

    [Test]
    public async Task CreateRepositoryAsync_TriesUserReposOnOrgNotFoundAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        // First request to /orgs/{org}/repos returns 404
        mockHandler.EnqueueNotFoundResponse();
        // Second request to /user/repos succeeds
        mockHandler.EnqueueCreatedResponse( SampleRepoJson );

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var options = new RepositoryCreateOptions
        {
            Owner = "user",
            Name = "test-repo",
            IsPrivate = true
        };

        var result = await provider.CreateRepositoryAsync( monitor, options );

        result.Success.ShouldBeTrue();
        mockHandler.Requests.Count.ShouldBe( 2 );
    }

    [Test]
    public async Task ArchiveRepositoryAsync_SuccessAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( SampleRepoJson.Replace( "\"archived\": false", "\"archived\": true" ) );

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.ArchiveRepositoryAsync( monitor, "test-owner", "test-repo" );

        result.Success.ShouldBeTrue();
        mockHandler.Requests.Count.ShouldBe( 1 );
        mockHandler.Requests[0].Method.ShouldBe( HttpMethod.Patch );
    }

    [Test]
    public async Task DeleteRepositoryAsync_SuccessAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueNoContentResponse();

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.DeleteRepositoryAsync( monitor, "test-owner", "test-repo" );

        result.Success.ShouldBeTrue();
        mockHandler.Requests.Count.ShouldBe( 1 );
        mockHandler.Requests[0].Method.ShouldBe( HttpMethod.Delete );
    }

    [Test]
    public async Task DeleteRepositoryAsync_ForbiddenAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueForbiddenResponse( "Must have admin rights to Repository" );

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.DeleteRepositoryAsync( monitor, "test-owner", "test-repo" );

        result.Success.ShouldBeFalse();
        result.IsAuthenticationError.ShouldBeTrue();
        result.ErrorMessage!.ShouldContain( "admin rights" );
    }

    [Test]
    public async Task RateLimited_ReturnsCorrectErrorAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueRateLimitResponse();

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

        result.Success.ShouldBeFalse();
        result.IsRateLimited.ShouldBeTrue();
    }

    [Test]
    public void Properties_AreCorrect()
    {
        using var provider = new GitHubProvider( "fake-pat" );

        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.GitHub );
        provider.InstanceId.ShouldBe( "github.com" );
        provider.BaseApiUrl.ToString().ShouldBe( "https://api.github.com/" );
    }

    [Test]
    public void Properties_Enterprise_AreCorrect()
    {
        using var provider = new GitHubProvider( "fake-pat", instanceId: "github.enterprise.com" );

        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.Unknown );
        provider.InstanceId.ShouldBe( "github.enterprise.com" );
        provider.BaseApiUrl.ToString().ShouldBe( "https://github.enterprise.com/api/v3/" );
    }

    [Test]
    public async Task Enterprise_ApiUrl_IsCorrectlyResolvedAsync()
    {
        // This test verifies that Enterprise API requests go to /api/v3/... not just /...
        // Catches the trailing-slash bug where absolute paths bypass the base URL path
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( SampleRepoJson );
        // Second request is to check if repo is empty (git/refs/heads)
        mockHandler.EnqueueJsonResponse( "[]" );

        using var provider = new GitHubProvider( "fake-pat", mockHandler, instanceId: "github.enterprise.com" );
        var monitor = new ActivityMonitor();

        await provider.GetRepositoryInfoAsync( monitor, "test-owner", "test-repo" );

        mockHandler.Requests.Count.ShouldBe( 2 );
        // First request: get repo info - must include /api/v3/
        var repoRequestUri = mockHandler.Requests[0].RequestUri!.ToString();
        repoRequestUri.ShouldBe( "https://github.enterprise.com/api/v3/repos/test-owner/test-repo" );
        // Second request: check if empty - must also include /api/v3/
        var emptyCheckUri = mockHandler.Requests[1].RequestUri!.ToString();
        emptyCheckUri.ShouldBe( "https://github.enterprise.com/api/v3/repos/test-owner/test-repo/git/refs/heads" );
    }
}
