using CK.Core;
using CKli.Core.GitHosting.Providers;
using CKli.Core.Tests.GitHosting.Mocks;
using NUnit.Framework;
using Shouldly;
using System;
using System.Threading.Tasks;

namespace CKli.Core.Tests.GitHosting;

/// <summary>
/// Tests that all RepositoryInfo fields are correctly mapped from API responses.
/// </summary>
[TestFixture]
public class RepositoryInfoMappingTests
{
    #region GitHub Field Mapping

    [Test]
    public async Task GitHub_maps_all_repository_fieldsAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        var gitHubJson = """
            {
                "id": 12345,
                "name": "my-repo",
                "full_name": "my-org/my-repo",
                "owner": { "login": "my-org" },
                "description": "My repository description",
                "private": true,
                "archived": true,
                "default_branch": "develop",
                "clone_url": "https://github.com/my-org/my-repo.git",
                "ssh_url": "git@github.com:my-org/my-repo.git",
                "html_url": "https://github.com/my-org/my-repo",
                "created_at": "2023-06-15T10:30:00Z",
                "updated_at": "2024-01-20T14:45:00Z"
            }
            """;
        mockHandler.EnqueueJsonResponse( gitHubJson );
        // For IsEmpty check - return refs to indicate not empty
        mockHandler.EnqueueJsonResponse( """[{"ref":"refs/heads/develop"}]""" );

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "my-org", "my-repo" );

        result.Success.ShouldBeTrue();
        var info = result.Data!;

        info.Owner.ShouldBe( "my-org" );
        info.Name.ShouldBe( "my-repo" );
        info.Description.ShouldBe( "My repository description" );
        info.IsPrivate.ShouldBeTrue();
        info.IsArchived.ShouldBeTrue();
        info.IsEmpty.ShouldBeFalse();
        info.DefaultBranch.ShouldBe( "develop" );
        info.CloneUrlHttps.ShouldBe( "https://github.com/my-org/my-repo.git" );
        info.CloneUrlSsh.ShouldBe( "git@github.com:my-org/my-repo.git" );
        info.WebUrl.ShouldBe( "https://github.com/my-org/my-repo" );
        info.CreatedAt.ShouldBe( new DateTime( 2023, 6, 15, 10, 30, 0, DateTimeKind.Utc ) );
        info.UpdatedAt.ShouldBe( new DateTime( 2024, 1, 20, 14, 45, 0, DateTimeKind.Utc ) );
    }

    [Test]
    public async Task GitHub_maps_public_repository_correctlyAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        var gitHubJson = """
            {
                "id": 12345,
                "name": "public-repo",
                "full_name": "user/public-repo",
                "owner": { "login": "user" },
                "description": null,
                "private": false,
                "archived": false,
                "default_branch": "main",
                "clone_url": "https://github.com/user/public-repo.git",
                "ssh_url": "git@github.com:user/public-repo.git",
                "html_url": "https://github.com/user/public-repo",
                "created_at": "2024-01-01T00:00:00Z",
                "updated_at": "2024-01-01T00:00:00Z"
            }
            """;
        mockHandler.EnqueueJsonResponse( gitHubJson );
        mockHandler.EnqueueJsonResponse( """[{"ref":"refs/heads/main"}]""" );

        using var provider = new GitHubProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "user", "public-repo" );

        result.Success.ShouldBeTrue();
        result.Data!.IsPrivate.ShouldBeFalse();
        result.Data!.IsArchived.ShouldBeFalse();
        result.Data!.Description.ShouldBeNull();
    }

    #endregion

    #region GitLab Field Mapping

    [Test]
    public async Task GitLab_maps_all_project_fieldsAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        // GitLab uses "path" for the repo name in mapping, not "name"
        var gitLabJson = """
            {
                "id": 12345,
                "name": "My Project Display Name",
                "path": "my-project",
                "path_with_namespace": "my-group/my-project",
                "namespace": { "full_path": "my-group" },
                "description": "My project description",
                "visibility": "private",
                "archived": true,
                "default_branch": "develop",
                "http_url_to_repo": "https://gitlab.com/my-group/my-project.git",
                "ssh_url_to_repo": "git@gitlab.com:my-group/my-project.git",
                "web_url": "https://gitlab.com/my-group/my-project",
                "created_at": "2023-06-15T10:30:00Z",
                "last_activity_at": "2024-01-20T14:45:00Z",
                "empty_repo": false
            }
            """;
        mockHandler.EnqueueJsonResponse( gitLabJson );

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "my-group", "my-project" );

        result.Success.ShouldBeTrue();
        var info = result.Data!;

        info.Owner.ShouldBe( "my-group" );
        info.Name.ShouldBe( "my-project" );  // Uses "path" not "name"
        info.Description.ShouldBe( "My project description" );
        info.IsPrivate.ShouldBeTrue();
        info.IsArchived.ShouldBeTrue();
        info.IsEmpty.ShouldBeFalse();
        info.DefaultBranch.ShouldBe( "develop" );
        info.CloneUrlHttps.ShouldBe( "https://gitlab.com/my-group/my-project.git" );
        info.CloneUrlSsh.ShouldBe( "git@gitlab.com:my-group/my-project.git" );
        info.WebUrl.ShouldBe( "https://gitlab.com/my-group/my-project" );
        info.CreatedAt.ShouldNotBeNull();
        info.UpdatedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task GitLab_maps_public_visibility_correctlyAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        var gitLabJson = """
            {
                "id": 12345,
                "name": "public-project",
                "path_with_namespace": "group/public-project",
                "namespace": { "full_path": "group" },
                "description": null,
                "visibility": "public",
                "archived": false,
                "default_branch": "main",
                "http_url_to_repo": "https://gitlab.com/group/public-project.git",
                "ssh_url_to_repo": "git@gitlab.com:group/public-project.git",
                "web_url": "https://gitlab.com/group/public-project",
                "created_at": "2024-01-01T00:00:00Z",
                "last_activity_at": "2024-01-01T00:00:00Z",
                "empty_repo": false
            }
            """;
        mockHandler.EnqueueJsonResponse( gitLabJson );

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "group", "public-project" );

        result.Success.ShouldBeTrue();
        result.Data!.IsPrivate.ShouldBeFalse();
    }

    [Test]
    public async Task GitLab_maps_internal_visibility_as_not_publicAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        var gitLabJson = """
            {
                "id": 12345,
                "name": "internal-project",
                "path_with_namespace": "group/internal-project",
                "namespace": { "full_path": "group" },
                "visibility": "internal",
                "archived": false,
                "default_branch": "main",
                "http_url_to_repo": "https://gitlab.com/group/internal-project.git",
                "ssh_url_to_repo": "git@gitlab.com:group/internal-project.git",
                "web_url": "https://gitlab.com/group/internal-project",
                "created_at": "2024-01-01T00:00:00Z",
                "last_activity_at": "2024-01-01T00:00:00Z",
                "empty_repo": false
            }
            """;
        mockHandler.EnqueueJsonResponse( gitLabJson );

        using var provider = new GitLabProvider( "fake-pat", mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "group", "internal-project" );

        result.Success.ShouldBeTrue();
        // Internal visibility should be treated as private (not public)
        result.Data!.IsPrivate.ShouldBeTrue();
    }

    #endregion

    #region Gitea Field Mapping

    [Test]
    public async Task Gitea_maps_all_repository_fieldsAsync()
    {
        var mockHandler = new MockHttpMessageHandler();
        var giteaJson = """
            {
                "id": 12345,
                "name": "my-repo",
                "full_name": "my-org/my-repo",
                "owner": { "login": "my-org" },
                "description": "My repository description",
                "private": true,
                "archived": true,
                "default_branch": "develop",
                "clone_url": "https://gitea.example.com/my-org/my-repo.git",
                "ssh_url": "git@gitea.example.com:my-org/my-repo.git",
                "html_url": "https://gitea.example.com/my-org/my-repo",
                "created_at": "2023-06-15T10:30:00Z",
                "updated_at": "2024-01-20T14:45:00Z",
                "empty": false
            }
            """;
        mockHandler.EnqueueJsonResponse( giteaJson );

        using var provider = new GiteaProvider( "fake-pat", new Uri( "https://gitea.example.com/api/v1/" ), mockHandler );
        var monitor = new ActivityMonitor();

        var result = await provider.GetRepositoryInfoAsync( monitor, "my-org", "my-repo" );

        result.Success.ShouldBeTrue();
        var info = result.Data!;

        info.Owner.ShouldBe( "my-org" );
        info.Name.ShouldBe( "my-repo" );
        info.Description.ShouldBe( "My repository description" );
        info.IsPrivate.ShouldBeTrue();
        info.IsArchived.ShouldBeTrue();
        info.IsEmpty.ShouldBeFalse();
        info.DefaultBranch.ShouldBe( "develop" );
        info.CloneUrlHttps.ShouldBe( "https://gitea.example.com/my-org/my-repo.git" );
        info.CloneUrlSsh.ShouldBe( "git@gitea.example.com:my-org/my-repo.git" );
        info.WebUrl.ShouldBe( "https://gitea.example.com/my-org/my-repo" );
        info.CreatedAt.ShouldNotBeNull();
        info.UpdatedAt.ShouldNotBeNull();
    }

    #endregion
}
