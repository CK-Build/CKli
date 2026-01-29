//using CK.Core;
//using CKli.Core.GitHosting.Providers;
//using CKli.Core.Tests.GitHosting.Mocks;
//using NUnit.Framework;
//using Shouldly;
//using System.Net;
//using System.Threading.Tasks;

//namespace CKli.Core.Tests.GitHosting;

///// <summary>
///// Tests for repository empty state detection (IsEmpty flag).
///// </summary>
//[TestFixture]
//public class RepositoryEmptyStateTests
//{
//    const string SampleRepoJson = """
//        {
//            "id": 12345,
//            "name": "test-repo",
//            "full_name": "test-owner/test-repo",
//            "owner": { "login": "test-owner" },
//            "description": "A test repository",
//            "private": true,
//            "archived": false,
//            "default_branch": "main",
//            "clone_url": "https://github.com/test-owner/test-repo.git",
//            "ssh_url": "git@github.com:test-owner/test-repo.git",
//            "html_url": "https://github.com/test-owner/test-repo",
//            "created_at": "2024-01-01T00:00:00Z",
//            "updated_at": "2024-01-01T00:00:00Z"
//        }
//        """;

//    #region GitHub Empty State Detection

//    [Test]
//    public async Task GitHub_GetRepository_detects_empty_repo_via_409_conflictAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        // First request: get repo info (succeeds)
//        mockHandler.EnqueueJsonResponse( SampleRepoJson );
//        // Second request: check refs - 409 Conflict means empty
//        mockHandler.EnqueueResponse( HttpStatusCode.Conflict, """{"message":"Git Repository is empty."}""" );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var result = await provider.GetRepositoryInfoAsync( monitor, "test-owner", "test-repo" );

//        result.Success.ShouldBeTrue();
//        result.Data!.IsEmpty.ShouldBeTrue();
//    }

//    [Test]
//    public async Task GitHub_GetRepository_detects_empty_repo_via_404Async()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        // First request: get repo info (succeeds)
//        mockHandler.EnqueueJsonResponse( SampleRepoJson );
//        // Second request: check refs - 404 also indicates empty in some cases
//        mockHandler.EnqueueNotFoundResponse();

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var result = await provider.GetRepositoryInfoAsync( monitor, "test-owner", "test-repo" );

//        result.Success.ShouldBeTrue();
//        result.Data!.IsEmpty.ShouldBeTrue();
//    }

//    [Test]
//    public async Task GitHub_GetRepository_detects_non_empty_repo_via_200Async()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        // First request: get repo info (succeeds)
//        mockHandler.EnqueueJsonResponse( SampleRepoJson );
//        // Second request: check refs - 200 with refs means not empty
//        mockHandler.EnqueueJsonResponse( """[{"ref":"refs/heads/main","object":{"sha":"abc123"}}]""" );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var result = await provider.GetRepositoryInfoAsync( monitor, "test-owner", "test-repo" );

//        result.Success.ShouldBeTrue();
//        result.Data!.IsEmpty.ShouldBeFalse();
//    }

//    #endregion

//    #region GitLab Empty State Detection

//    [Test]
//    public async Task GitLab_GetRepository_maps_empty_repo_flagAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        // GitLab includes empty_repo in response
//        var gitLabJson = """
//            {
//                "id": 12345,
//                "name": "test-repo",
//                "path_with_namespace": "test-group/test-repo",
//                "namespace": { "full_path": "test-group" },
//                "description": "A test repository",
//                "visibility": "private",
//                "archived": false,
//                "default_branch": "main",
//                "http_url_to_repo": "https://gitlab.com/test-group/test-repo.git",
//                "ssh_url_to_repo": "git@gitlab.com:test-group/test-repo.git",
//                "web_url": "https://gitlab.com/test-group/test-repo",
//                "created_at": "2024-01-01T00:00:00Z",
//                "last_activity_at": "2024-01-01T00:00:00Z",
//                "empty_repo": true
//            }
//            """;
//        mockHandler.EnqueueJsonResponse( gitLabJson );

//        using var provider = new GitLabProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var result = await provider.GetRepositoryInfoAsync( monitor, "test-group", "test-repo" );

//        result.Success.ShouldBeTrue();
//        result.Data!.IsEmpty.ShouldBeTrue();
//    }

//    [Test]
//    public async Task GitLab_GetRepository_maps_non_empty_repo_flagAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        var gitLabJson = """
//            {
//                "id": 12345,
//                "name": "test-repo",
//                "path_with_namespace": "test-group/test-repo",
//                "namespace": { "full_path": "test-group" },
//                "description": "A test repository",
//                "visibility": "private",
//                "archived": false,
//                "default_branch": "main",
//                "http_url_to_repo": "https://gitlab.com/test-group/test-repo.git",
//                "ssh_url_to_repo": "git@gitlab.com:test-group/test-repo.git",
//                "web_url": "https://gitlab.com/test-group/test-repo",
//                "created_at": "2024-01-01T00:00:00Z",
//                "last_activity_at": "2024-01-01T00:00:00Z",
//                "empty_repo": false
//            }
//            """;
//        mockHandler.EnqueueJsonResponse( gitLabJson );

//        using var provider = new GitLabProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var result = await provider.GetRepositoryInfoAsync( monitor, "test-group", "test-repo" );

//        result.Success.ShouldBeTrue();
//        result.Data!.IsEmpty.ShouldBeFalse();
//    }

//    #endregion

//    #region Gitea Empty State Detection

//    [Test]
//    public async Task Gitea_GetRepository_maps_empty_flagAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        var giteaJson = """
//            {
//                "id": 12345,
//                "name": "test-repo",
//                "full_name": "test-owner/test-repo",
//                "owner": { "login": "test-owner" },
//                "description": "A test repository",
//                "private": true,
//                "archived": false,
//                "default_branch": "main",
//                "clone_url": "https://gitea.example.com/test-owner/test-repo.git",
//                "ssh_url": "git@gitea.example.com:test-owner/test-repo.git",
//                "html_url": "https://gitea.example.com/test-owner/test-repo",
//                "created_at": "2024-01-01T00:00:00Z",
//                "updated_at": "2024-01-01T00:00:00Z",
//                "empty": true
//            }
//            """;
//        mockHandler.EnqueueJsonResponse( giteaJson );

//        using var provider = new GiteaProvider( "fake-pat", new System.Uri( "https://gitea.example.com/api/v1/" ), mockHandler );
//        var monitor = new ActivityMonitor();

//        var result = await provider.GetRepositoryInfoAsync( monitor, "test-owner", "test-repo" );

//        result.Success.ShouldBeTrue();
//        result.Data!.IsEmpty.ShouldBeTrue();
//    }

//    #endregion
//}
