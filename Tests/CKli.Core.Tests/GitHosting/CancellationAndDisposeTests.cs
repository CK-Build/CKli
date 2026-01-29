//using CK.Core;
//using CKli.Core.GitHosting;
//using CKli.Core.GitHosting.Providers;
//using CKli.Core.Tests.GitHosting.Mocks;
//using NUnit.Framework;
//using Shouldly;
//using System;
//using System.Threading.Tasks;

//namespace CKli.Core.Tests.GitHosting;

///// <summary>
///// Tests for dispose pattern and basic provider lifecycle.
///// </summary>
//[TestFixture]
//public class DisposePatternTests
//{
//    #region Dispose Pattern Tests

//    [Test]
//    public void GitHub_provider_can_be_disposed_multiple_times()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        var provider = new GitHubProvider( "fake-pat", mockHandler );

//        // Should not throw
//        provider.Dispose();
//        provider.Dispose();
//    }

//    [Test]
//    public void GitLab_provider_can_be_disposed_multiple_times()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        var provider = new GitLabProvider( "fake-pat", mockHandler );

//        // Should not throw
//        provider.Dispose();
//        provider.Dispose();
//    }

//    [Test]
//    public void Gitea_provider_can_be_disposed_multiple_times()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        var provider = new GiteaProvider( "fake-pat", new Uri( "https://gitea.example.com/api/v1/" ), mockHandler );

//        // Should not throw
//        provider.Dispose();
//        provider.Dispose();
//    }

//    #endregion

//    #region Using Statement Tests

//    [Test]
//    public async Task Provider_works_correctly_within_using_blockAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        mockHandler.EnqueueJsonResponse( """
//            {
//                "id": 1,
//                "name": "repo",
//                "full_name": "owner/repo",
//                "owner": { "login": "owner" },
//                "private": false,
//                "archived": false,
//                "default_branch": "main",
//                "clone_url": "https://github.com/owner/repo.git",
//                "ssh_url": "git@github.com:owner/repo.git",
//                "html_url": "https://github.com/owner/repo"
//            }
//            """ );
//        mockHandler.EnqueueJsonResponse( "[]" ); // refs check

//        var monitor = new ActivityMonitor();
//        GitHostingOperationResult<HostedRepositoryInfo>? result = null;

//        using( var provider = new GitHubProvider( "fake-pat", mockHandler ) )
//        {
//            result = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );
//        }

//        result.ShouldNotBeNull();
//        result!.Success.ShouldBeTrue();
//    }

//    [Test]
//    public async Task Multiple_operations_on_same_provider_workAsync()
//    {
//        var mockHandler = new MockHttpMessageHandler();
//        // First operation responses
//        mockHandler.EnqueueJsonResponse( """
//            {
//                "id": 1,
//                "name": "repo1",
//                "full_name": "owner/repo1",
//                "owner": { "login": "owner" },
//                "private": false,
//                "archived": false,
//                "default_branch": "main",
//                "clone_url": "https://github.com/owner/repo1.git",
//                "ssh_url": "git@github.com:owner/repo1.git",
//                "html_url": "https://github.com/owner/repo1"
//            }
//            """ );
//        mockHandler.EnqueueJsonResponse( "[]" ); // refs check for first repo
//        // Second operation responses
//        mockHandler.EnqueueJsonResponse( """
//            {
//                "id": 2,
//                "name": "repo2",
//                "full_name": "owner/repo2",
//                "owner": { "login": "owner" },
//                "private": true,
//                "archived": false,
//                "default_branch": "main",
//                "clone_url": "https://github.com/owner/repo2.git",
//                "ssh_url": "git@github.com:owner/repo2.git",
//                "html_url": "https://github.com/owner/repo2"
//            }
//            """ );
//        mockHandler.EnqueueJsonResponse( "[]" ); // refs check for second repo

//        var monitor = new ActivityMonitor();

//        using var provider = new GitHubProvider( "fake-pat", mockHandler );

//        var result1 = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo1" );
//        var result2 = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo2" );

//        result1.Success.ShouldBeTrue();
//        result1.Data!.Name.ShouldBe( "repo1" );

//        result2.Success.ShouldBeTrue();
//        result2.Data!.Name.ShouldBe( "repo2" );
//    }

//    #endregion
//}
