//using CK.Core;
//using CKli.Core.GitHosting;
//using CKli.Core.GitHosting.Providers;
//using CKli.Core.Tests.GitHosting.Mocks;
//using NUnit.Framework;
//using Shouldly;
//using System.Text.Json;
//using System.Threading.Tasks;

//namespace CKli.Core.Tests.GitHosting;

///// <summary>
///// Tests for RepositoryCreateOptions and its handling by providers.
///// </summary>
//[TestFixture]
//public class RepositoryCreateOptionsTests
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

//    #region GitHub CreateRepository Options

//    [Test]
//    public async Task GitHub_CreateRepository_sends_auto_initAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueCreatedResponse( SampleRepoJson );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var options = new HostedRepositoryCreateOptions
//        {
//            Owner = "test-owner",
//            Name = "test-repo",
//            AutoInit = true
//        };

//        await provider.CreateRepositoryAsync( monitor, options );

//        var requestBody = await mockHandler.GetRequestBodyAsync();
//        requestBody.ShouldNotBeNull();
//        requestBody.ShouldContain( "\"auto_init\":true" );
//    }

//    [Test]
//    public async Task GitHub_CreateRepository_sends_gitignore_templateAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueCreatedResponse( SampleRepoJson );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var options = new HostedRepositoryCreateOptions
//        {
//            Owner = "test-owner",
//            Name = "test-repo",
//            GitIgnoreTemplate = "VisualStudio"
//        };

//        await provider.CreateRepositoryAsync( monitor, options );

//        var requestBody = await mockHandler.GetRequestBodyAsync();
//        requestBody.ShouldNotBeNull();
//        requestBody.ShouldContain( "VisualStudio" );
//    }

//    [Test]
//    public async Task GitHub_CreateRepository_sends_license_templateAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueCreatedResponse( SampleRepoJson );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var options = new HostedRepositoryCreateOptions
//        {
//            Owner = "test-owner",
//            Name = "test-repo",
//            LicenseTemplate = "MIT"
//        };

//        await provider.CreateRepositoryAsync( monitor, options );

//        var requestBody = await mockHandler.GetRequestBodyAsync();
//        requestBody.ShouldNotBeNull();
//        requestBody.ShouldContain( "MIT" );
//    }

//    #endregion

//    #region Conflict (409) - Repository Already Exists

//    [Test]
//    public async Task GitHub_CreateRepository_returns_failure_on_conflictAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueResponse( System.Net.HttpStatusCode.Conflict,
//            """{"message":"Repository creation failed.","errors":[{"resource":"Repository","code":"custom","field":"name","message":"name already exists on this account"}]}""" );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var options = new HostedRepositoryCreateOptions
//        {
//            Owner = "test-owner",
//            Name = "existing-repo"
//        };

//        var result = await provider.CreateRepositoryAsync( monitor, options );

//        result.Success.ShouldBeFalse();
//        result.HttpStatusCode.ShouldBe( 409 );
//        result.ErrorMessage!.ShouldContain( "already exists" );
//    }

//    [Test]
//    public async Task GitLab_CreateRepository_returns_failure_on_conflictAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        // GitLab namespace lookup
//        mockHandler.EnqueueNotFoundResponse();
//        // GitLab returns 400 with error message for duplicate
//        mockHandler.EnqueueResponse( System.Net.HttpStatusCode.BadRequest,
//            """{"message":{"name":["has already been taken"]}}""" );

//        using var provider = new GitLabProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var options = new HostedRepositoryCreateOptions
//        {
//            Owner = "test-group",
//            Name = "existing-repo"
//        };

//        var result = await provider.CreateRepositoryAsync( monitor, options );

//        result.Success.ShouldBeFalse();
//    }

//    #endregion

//    #region Special Characters in Names

//    [Test]
//    public async Task GitHub_handles_repo_name_with_dotsAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        var repoJson = SampleRepoJson.Replace( "test-repo", "my.dotted.repo" );
//        mockHandler.EnqueueJsonResponse( repoJson );
//        // For IsEmpty check
//        mockHandler.EnqueueResponse( System.Net.HttpStatusCode.Conflict, "" );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var result = await provider.GetRepositoryInfoAsync( monitor, "owner", "my.dotted.repo" );

//        result.Success.ShouldBeTrue();
//        result.Data!.Name.ShouldBe( "my.dotted.repo" );
//    }

//    [Test]
//    public async Task GitHub_handles_owner_with_hyphensAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        var repoJson = SampleRepoJson.Replace( "test-owner", "my-org-name" );
//        mockHandler.EnqueueJsonResponse( repoJson );
//        mockHandler.EnqueueResponse( System.Net.HttpStatusCode.Conflict, "" );

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );
//        var monitor = new ActivityMonitor();

//        var result = await provider.GetRepositoryInfoAsync( monitor, "my-org-name", "test-repo" );

//        result.Success.ShouldBeTrue();
//        result.Data!.Owner.ShouldBe( "my-org-name" );
//    }

//    #endregion
//}
