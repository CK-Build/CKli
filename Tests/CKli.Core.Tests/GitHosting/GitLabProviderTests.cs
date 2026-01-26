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
public class GitLabProviderTests
{
    static readonly string SampleProjectJson = """
        {
            "id": 12345,
            "name": "test-repo",
            "path": "test-repo",
            "path_with_namespace": "test-group/test-repo",
            "description": "A test repository",
            "visibility": "private",
            "archived": false,
            "empty_repo": false,
            "default_branch": "main",
            "http_url_to_repo": "https://gitlab.com/test-group/test-repo.git",
            "ssh_url_to_repo": "git@gitlab.com:test-group/test-repo.git",
            "web_url": "https://gitlab.com/test-group/test-repo",
            "created_at": "2024-01-01T00:00:00Z",
            "last_activity_at": "2024-01-02T00:00:00Z",
            "namespace": {
                "id": 100,
                "name": "test-group",
                "path": "test-group",
                "full_path": "test-group",
                "kind": "group"
            }
        }
        """;

    static readonly string SampleNestedProjectJson = """
        {
            "id": 12346,
            "name": "test-repo",
            "path": "test-repo",
            "path_with_namespace": "parent/child/test-repo",
            "description": "A nested group repository",
            "visibility": "private",
            "archived": false,
            "empty_repo": false,
            "default_branch": "main",
            "http_url_to_repo": "https://gitlab.com/parent/child/test-repo.git",
            "ssh_url_to_repo": "git@gitlab.com:parent/child/test-repo.git",
            "web_url": "https://gitlab.com/parent/child/test-repo",
            "created_at": "2024-01-01T00:00:00Z",
            "last_activity_at": "2024-01-02T00:00:00Z",
            "namespace": {
                "id": 101,
                "name": "child",
                "path": "child",
                "full_path": "parent/child",
                "kind": "group"
            }
        }
        """;

    [Test]
    public void ParseRemoteUrl_HttpsUrl()
    {
        using var provider = new GitLabProvider( "fake-pat" );

        var result = provider.ParseRemoteUrl( "https://gitlab.com/test-group/test-repo" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "test-group" );
        result.Value.RepoName.ShouldBe( "test-repo" );
    }

    [Test]
    public void ParseRemoteUrl_SshUrl()
    {
        using var provider = new GitLabProvider( "fake-pat" );

        var result = provider.ParseRemoteUrl( "git@gitlab.com:test-group/test-repo.git" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "test-group" );
        result.Value.RepoName.ShouldBe( "test-repo" );
    }

    [Test]
    public void ParseRemoteUrl_NestedGroup()
    {
        using var provider = new GitLabProvider( "fake-pat" );

        var result = provider.ParseRemoteUrl( "https://gitlab.com/parent/child/grandchild/test-repo" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "parent/child/grandchild" );
        result.Value.RepoName.ShouldBe( "test-repo" );
    }

    [Test]
    public void ParseRemoteUrl_ReturnsNullForOtherHosts()
    {
        using var provider = new GitLabProvider( "fake-pat" );

        var result = provider.ParseRemoteUrl( "https://github.com/owner/repo" );

        result.ShouldBeNull();
    }

    [Test]
    public async Task GetRepositoryInfoAsync_SuccessAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( SampleProjectJson );

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "test-group", "test-repo" );

        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Owner.ShouldBe( "test-group" );
        result.Data.Name.ShouldBe( "test-repo" );
        result.Data.Description.ShouldBe( "A test repository" );
        result.Data.IsPrivate.ShouldBeTrue();
        result.Data.DefaultBranch.ShouldBe( "main" );
    }

    [Test]
    public async Task GetRepositoryInfoAsync_NestedGroupAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( SampleNestedProjectJson );

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "parent/child", "test-repo" );

        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Owner.ShouldBe( "parent/child" );
        result.Data.Name.ShouldBe( "test-repo" );
    }

    [Test]
    public async Task GetRepositoryInfoAsync_NotFoundAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueNotFoundResponse();

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "group", "non-existent" );

        result.Success.ShouldBeFalse();
        result.IsNotFound.ShouldBeTrue();
    }

    [Test]
    public async Task GetRepositoryInfoAsync_UnauthorizedAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueUnauthorizedResponse();

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "group", "private-repo" );

        result.Success.ShouldBeFalse();
        result.IsAuthenticationError.ShouldBeTrue();
    }

    [Test]
    public async Task CreateRepositoryAsync_SuccessAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        // First request: namespace lookup (can return 404 - namespace not found, will use current user)
        mockHandler.EnqueueNotFoundResponse();
        // Second request: project creation
        mockHandler.EnqueueCreatedResponse( SampleProjectJson );

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var options = new RepositoryCreateOptions
        {
            Owner = "test-group",
            Name = "test-repo",
            Description = "A test repository",
            IsPrivate = true
        };

        var result = await provider.CreateRepositoryAsync( monitor, options );

        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Name.ShouldBe( "test-repo" );
        mockHandler.Requests.Count.ShouldBe( 2 );
    }

    [Test]
    public async Task ArchiveRepositoryAsync_SuccessAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( SampleProjectJson.Replace( "\"archived\": false", "\"archived\": true" ) );

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.ArchiveRepositoryAsync( monitor, "test-group", "test-repo" );

        result.Success.ShouldBeTrue();
        mockHandler.Requests.Count.ShouldBe( 1 );
        mockHandler.Requests[0].Method.ShouldBe( HttpMethod.Post );
    }

    [Test]
    public async Task DeleteRepositoryAsync_SuccessAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueNoContentResponse();

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.DeleteRepositoryAsync( monitor, "test-group", "test-repo" );

        result.Success.ShouldBeTrue();
        mockHandler.Requests.Count.ShouldBe( 1 );
        mockHandler.Requests[0].Method.ShouldBe( HttpMethod.Delete );
    }

    [Test]
    public async Task DeleteRepositoryAsync_ForbiddenAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueForbiddenResponse( "403 Forbidden" );

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.DeleteRepositoryAsync( monitor, "test-group", "test-repo" );

        result.Success.ShouldBeFalse();
        result.IsAuthenticationError.ShouldBeTrue();
    }

    [Test]
    public async Task RateLimited_ReturnsCorrectErrorAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueRateLimitResponse();

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "group", "repo" );

        result.Success.ShouldBeFalse();
        result.IsRateLimited.ShouldBeTrue();
    }

    [Test]
    public void Properties_AreCorrect()
    {
        using var provider = new GitLabProvider( "fake-pat" );

        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.GitLab );
        provider.InstanceId.ShouldBe( "gitlab.com" );
        // BaseApiUrl should have trailing slash for proper relative URL resolution
        provider.BaseApiUrl.ToString().ShouldBe( "https://gitlab.com/api/v4/" );
    }

    [Test]
    public void Properties_SelfHosted_AreCorrect()
    {
        using var provider = new GitLabProvider( "fake-pat", instanceId: "gitlab.internal.org" );

        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.Unknown );
        provider.InstanceId.ShouldBe( "gitlab.internal.org" );
        provider.BaseApiUrl.ToString().ShouldBe( "https://gitlab.internal.org/api/v4/" );
    }

    [Test]
    public async Task ApiUrl_IsCorrectlyResolved_WithApiPathAsync()
    {
        // This test verifies that API requests go to /api/v4/... and not just /...
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueJsonResponse( SampleProjectJson );

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        await provider.GetRepositoryInfoAsync( monitor, "test-group", "test-repo" );

        mockHandler.Requests.Count.ShouldBe( 1 );
        var requestUri = mockHandler.Requests[0].RequestUri!.ToString();
        // Verify the request URL includes the /api/v4/ path
        requestUri.ShouldContain( "/api/v4/" );
    }
}
