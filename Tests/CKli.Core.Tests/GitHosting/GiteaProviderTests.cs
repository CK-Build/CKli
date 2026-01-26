using System;
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
public class GiteaProviderTests
{
    static readonly Uri TestApiUrl = new( "https://gitea.test.com/api/v1/" );

    static readonly string SampleRepoJson = """
        {
            "id": 12345,
            "name": "test-repo",
            "full_name": "test-owner/test-repo",
            "description": "A test repository",
            "private": true,
            "archived": false,
            "empty": false,
            "default_branch": "main",
            "clone_url": "https://gitea.test.com/test-owner/test-repo.git",
            "ssh_url": "git@gitea.test.com:test-owner/test-repo.git",
            "html_url": "https://gitea.test.com/test-owner/test-repo",
            "created_at": "2024-01-01T00:00:00Z",
            "updated_at": "2024-01-02T00:00:00Z",
            "owner": {
                "id": 1,
                "login": "test-owner",
                "username": "test-owner"
            }
        }
        """;

    [Test]
    public void ParseRemoteUrl_HttpsUrl()
    {
        using var provider = new GiteaProvider( "fake-pat", TestApiUrl );

        var result = provider.ParseRemoteUrl( "https://gitea.test.com/test-owner/test-repo" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "test-owner" );
        result.Value.RepoName.ShouldBe( "test-repo" );
    }

    [Test]
    public void ParseRemoteUrl_HttpsUrl_WithGitExtension()
    {
        using var provider = new GiteaProvider( "fake-pat", TestApiUrl );

        var result = provider.ParseRemoteUrl( "https://gitea.test.com/test-owner/test-repo.git" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "test-owner" );
        result.Value.RepoName.ShouldBe( "test-repo" );
    }

    [Test]
    public void ParseRemoteUrl_SshUrl()
    {
        using var provider = new GiteaProvider( "fake-pat", TestApiUrl );

        var result = provider.ParseRemoteUrl( "git@gitea.test.com:test-owner/test-repo.git" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "test-owner" );
        result.Value.RepoName.ShouldBe( "test-repo" );
    }

    [Test]
    public void ParseRemoteUrl_ReturnsNullForOtherHosts()
    {
        using var provider = new GiteaProvider( "fake-pat", TestApiUrl );

        var result = provider.ParseRemoteUrl( "https://github.com/owner/repo" );

        result.ShouldBeNull();
    }

    [Test]
    public async Task GetRepositoryInfoAsync_SuccessAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( SampleRepoJson );

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
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

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
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

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
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

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
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

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
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

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
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

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
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
        mockHandler.EnqueueForbiddenResponse( "You do not have access to delete the repository" );

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.DeleteRepositoryAsync( monitor, "test-owner", "test-repo" );

        result.Success.ShouldBeFalse();
        result.IsAuthenticationError.ShouldBeTrue();
        result.ErrorMessage!.ShouldContain( "do not have access" );
    }

    [Test]
    public async Task RateLimited_ReturnsCorrectErrorAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueRateLimitResponse();

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

        result.Success.ShouldBeFalse();
        result.IsRateLimited.ShouldBeTrue();
    }

    [Test]
    public void Properties_AreCorrect()
    {
        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, instanceId: "gitea.test.com" );

        // Gitea is always Unknown since it has no official cloud offering
        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.Unknown );
        provider.InstanceId.ShouldBe( "gitea.test.com" );
        // BaseApiUrl should have trailing slash for proper relative URL resolution
        provider.BaseApiUrl.ToString().ShouldBe( "https://gitea.test.com/api/v1/" );
    }

    [Test]
    public void InstanceId_DerivedFromHost_WhenNotProvided()
    {
        using var provider = new GiteaProvider( "fake-pat", TestApiUrl );

        provider.InstanceId.ShouldBe( "gitea.test.com" );
    }

    [Test]
    public async Task ApiUrl_IsCorrectlyResolved_WithApiPathAsync()
    {
        // This test verifies that API requests go to /api/v1/... and not just /...
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( SampleRepoJson );

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
        var monitor = new ActivityMonitor();

        await provider.GetRepositoryInfoAsync( monitor, "test-owner", "test-repo" );

        mockHandler.Requests.Count.ShouldBe( 1 );
        var requestUri = mockHandler.Requests[0].RequestUri!.ToString();
        // Verify the request URL includes the /api/v1/ path
        requestUri.ShouldContain( "/api/v1/" );
        requestUri.ShouldBe( "https://gitea.test.com/api/v1/repos/test-owner/test-repo" );
    }

    [Test]
    public async Task CreateRepository_ApiUrl_IsCorrectlyResolvedAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        // First request to orgs endpoint will fail
        mockHandler.EnqueueNotFoundResponse();
        // Second request to user/repos succeeds
        mockHandler.EnqueueCreatedResponse( SampleRepoJson );

        using var provider = new GiteaProvider( "fake-pat", TestApiUrl, mockHandler );
        var monitor = new ActivityMonitor();

        var options = new RepositoryCreateOptions
        {
            Owner = "test-owner",
            Name = "test-repo",
            IsPrivate = true
        };

        await provider.CreateRepositoryAsync( monitor, options );

        // First request should go to orgs endpoint with correct API path
        mockHandler.Requests[0].RequestUri!.ToString()
            .ShouldBe( "https://gitea.test.com/api/v1/orgs/test-owner/repos" );
        // Second request should go to user/repos with correct API path
        mockHandler.Requests[1].RequestUri!.ToString()
            .ShouldBe( "https://gitea.test.com/api/v1/user/repos" );
    }
}
