//using CK.Core;
//using CKli.Core.GitHosting;
//using CKli.Core.GitHosting.Providers;
//using CKli.Core.Tests.GitHosting.Mocks;
//using NUnit.Framework;
//using Shouldly;
//using System;
//using System.Linq;
//using System.Net.Http;
//using System.Threading.Tasks;

//namespace CKli.Core.Tests.GitHosting;

///// <summary>
///// Tests that verify HTTP requests are correctly constructed (URLs, headers, methods).
///// </summary>
//[TestFixture]
//public class HttpRequestVerificationTests
//{
//    const string SampleGitHubRepoJson = """
//        {
//            "id": 1,
//            "name": "repo",
//            "full_name": "owner/repo",
//            "owner": { "login": "owner" },
//            "private": false,
//            "archived": false,
//            "default_branch": "main",
//            "clone_url": "https://github.com/owner/repo.git",
//            "ssh_url": "git@github.com:owner/repo.git",
//            "html_url": "https://github.com/owner/repo"
//        }
//        """;

//    const string SampleGitLabProjectJson = """
//        {
//            "id": 1,
//            "name": "repo",
//            "path_with_namespace": "group/repo",
//            "namespace": { "full_path": "group" },
//            "visibility": "private",
//            "archived": false,
//            "default_branch": "main",
//            "http_url_to_repo": "https://gitlab.com/group/repo.git",
//            "ssh_url_to_repo": "git@gitlab.com:group/repo.git",
//            "web_url": "https://gitlab.com/group/repo",
//            "empty_repo": false
//        }
//        """;

//    #region GitHub Request URL Tests

//    [Test]
//    public async Task GitHub_GetRepository_uses_correct_urlAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueJsonResponse( SampleGitHubRepoJson );
//        mockHandler.EnqueueJsonResponse( "[]" ); // refs check

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        await provider.GetRepositoryInfoAsync( monitor, "my-owner", "my-repo" );

//        mockHandler.Requests[0].RequestUri!.ToString().ShouldBe( "https://api.github.com/repos/my-owner/my-repo" );
//    }

//    [Test]
//    public async Task GitHub_Enterprise_uses_correct_api_pathAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueJsonResponse( SampleGitHubRepoJson );
//        mockHandler.EnqueueJsonResponse( "[]" );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler, instanceId: "github.company.com" );
//        var monitor = new ActivityMonitor();

//        await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        var requestUrl = mockHandler.Requests[0].RequestUri!.ToString();
//        requestUrl.ShouldStartWith( "https://github.company.com/api/v3/" );
//        requestUrl.ShouldContain( "/repos/owner/repo" );
//    }

//    [Test]
//    public async Task GitHub_CreateRepository_posts_to_orgs_endpoint_firstAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueCreatedResponse( SampleGitHubRepoJson );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var options = new HostedRepositoryCreateOptions { Owner = "my-org", Name = "new-repo" };
//        await provider.CreateRepositoryAsync( monitor, options );

//        mockHandler.Requests[0].RequestUri!.ToString().ShouldContain( "/orgs/my-org/repos" );
//        mockHandler.Requests[0].Method.ShouldBe( HttpMethod.Post );
//    }

//    [Test]
//    public async Task GitHub_ArchiveRepository_uses_patch_methodAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueJsonResponse( SampleGitHubRepoJson );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        await provider.ArchiveRepositoryAsync( monitor, "owner", "repo" );

//        mockHandler.Requests[0].Method.ShouldBe( HttpMethod.Patch );
//        mockHandler.Requests[0].RequestUri!.ToString().ShouldContain( "/repos/owner/repo" );
//    }

//    [Test]
//    public async Task GitHub_DeleteRepository_uses_delete_methodAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueNoContentResponse();

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        await provider.DeleteRepositoryAsync( monitor, "owner", "repo" );

//        mockHandler.Requests[0].Method.ShouldBe( HttpMethod.Delete );
//    }

//    #endregion

//    #region GitLab Request URL Tests

//    [Test]
//    public async Task GitLab_GetRepository_uses_url_encoded_pathAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueJsonResponse( SampleGitLabProjectJson );

//        using var provider = new GitLabProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        await provider.GetRepositoryInfoAsync( monitor, "my-group/sub-group", "my-repo" );

//        var requestUrl = mockHandler.Requests[0].RequestUri!.ToString();
//        // GitLab uses URL-encoded path: my-group/sub-group/my-repo -> my-group%2Fsub-group%2Fmy-repo
//        // Note: URL encoding can be lowercase (%2f) or uppercase (%2F), both are valid
//        requestUrl.ToLowerInvariant().ShouldContain( "my-group%2fsub-group%2fmy-repo" );
//    }

//    [Test]
//    public async Task GitLab_ArchiveRepository_uses_post_to_archive_endpointAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueJsonResponse( SampleGitLabProjectJson );

//        using var provider = new GitLabProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        await provider.ArchiveRepositoryAsync( monitor, "group", "repo" );

//        mockHandler.Requests[0].Method.ShouldBe( HttpMethod.Post );
//        mockHandler.Requests[0].RequestUri!.ToString().ShouldContain( "/archive" );
//    }

//    #endregion

//    #region Gitea Request URL Tests

//    [Test]
//    public async Task Gitea_GetRepository_uses_correct_api_pathAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        var giteaJson = SampleGitHubRepoJson; // Gitea uses similar format to GitHub
//        mockHandler.EnqueueJsonResponse( giteaJson );

//        using var provider = new GiteaProvider( "fake-pat", new Uri( "https://gitea.example.com/api/v1/" ), mockHandler );
//        var monitor = new ActivityMonitor();

//        await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        var requestUrl = mockHandler.Requests[0].RequestUri!.ToString();
//        requestUrl.ShouldBe( "https://gitea.example.com/api/v1/repos/owner/repo" );
//    }

//    #endregion

//    #region Authorization Header Tests

//    [Test]
//    public async Task GitHub_sends_bearer_token_headerAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueJsonResponse( SampleGitHubRepoJson );
//        mockHandler.EnqueueJsonResponse( "[]" );

//        using var provider = new GitHubProvider( "my-secret-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        var authHeader = mockHandler.Requests[0].Headers.Authorization;
//        authHeader.ShouldNotBeNull();
//        authHeader!.Scheme.ShouldBe( "Bearer" );
//        authHeader.Parameter.ShouldBe( "my-secret-pat" );
//    }

//    [Test]
//    public async Task GitHub_sends_user_agent_headerAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueJsonResponse( SampleGitHubRepoJson );
//        mockHandler.EnqueueJsonResponse( "[]" );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        var userAgent = mockHandler.Requests[0].Headers.UserAgent.ToString();
//        userAgent.ShouldContain( "CKli" );
//    }

//    [Test]
//    public async Task GitHub_sends_accept_headerAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueJsonResponse( SampleGitHubRepoJson );
//        mockHandler.EnqueueJsonResponse( "[]" );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        var acceptHeader = mockHandler.Requests[0].Headers.Accept.ToString();
//        acceptHeader.ShouldContain( "application/vnd.github" );
//    }

//    #endregion
//}
