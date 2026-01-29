//using CK.Core;
//using CKli.Core.GitHosting;
//using CKli.Core.GitHosting.Providers;
//using NUnit.Framework;
//using Shouldly;
//using System.Collections.Generic;

//namespace CKli.Core.Tests.GitHosting;

///// <summary>
///// Tests PAT (Personal Access Token) key generation for various providers and hostnames.
///// These tests verify that the correct environment variable names are used for credential lookup.
///// </summary>
//[TestFixture]
//public class PatKeyGenerationTests
//{
//    // Mock secrets store that records which keys were requested
//    sealed class RecordingSecretsStore : ISecretsStore
//    {
//        public List<string[]> RequestedKeys { get; } = new();

//        public string? TryGetRequiredSecret( IActivityMonitor monitor, IEnumerable<string> keys )
//        {
//            var keyArray = keys is string[] arr ? arr : new List<string>( keys ).ToArray();
//            RequestedKeys.Add( keyArray );
//            return null; // Return null to indicate no secret found
//        }
//    }

//    #region GitHub PAT Key Tests

//    [Test]
//    public async System.Threading.Tasks.Task GitHub_cloud_uses_GITHUB_GIT_WRITE_PATAsync()
//    {
//        var secretsStore = new RecordingSecretsStore();
//        using var provider = new GitHubProvider( "github.com", new System.Uri( "https://api.github.com/" ), secretsStore );
//        var monitor = new ActivityMonitor();

//        // Trigger a request that needs credentials
//        _ = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        secretsStore.RequestedKeys.Count.ShouldBe( 1 );
//        secretsStore.RequestedKeys[0].ShouldContain( "GITHUB_GIT_WRITE_PAT" );
//    }

//    [Test]
//    public async System.Threading.Tasks.Task GitHub_enterprise_uses_sanitized_hostnameAsync()
//    {
//        var secretsStore = new RecordingSecretsStore();
//        using var provider = new GitHubProvider( "github.company.com", new System.Uri( "https://github.company.com/api/v3/" ), secretsStore );
//        var monitor = new ActivityMonitor();

//        _ = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        secretsStore.RequestedKeys.Count.ShouldBe( 1 );
//        // Dots become underscores, result is uppercase with _GIT_WRITE_PAT suffix
//        secretsStore.RequestedKeys[0].ShouldContain( "GITHUB_COMPANY_COM_GIT_WRITE_PAT" );
//    }

//    #endregion

//    #region GitLab PAT Key Tests

//    [Test]
//    public async System.Threading.Tasks.Task GitLab_cloud_uses_GITLAB_GIT_WRITE_PATAsync()
//    {
//        var secretsStore = new RecordingSecretsStore();
//        using var provider = new GitLabProvider( "gitlab.com", new System.Uri( "https://gitlab.com/api/v4/" ), secretsStore );
//        var monitor = new ActivityMonitor();

//        _ = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        secretsStore.RequestedKeys.Count.ShouldBe( 1 );
//        secretsStore.RequestedKeys[0].ShouldContain( "GITLAB_GIT_WRITE_PAT" );
//    }

//    [Test]
//    public async System.Threading.Tasks.Task GitLab_self_hosted_uses_sanitized_hostnameAsync()
//    {
//        var secretsStore = new RecordingSecretsStore();
//        using var provider = new GitLabProvider( "gitlab.internal.org", new System.Uri( "https://gitlab.internal.org/api/v4/" ), secretsStore );
//        var monitor = new ActivityMonitor();

//        _ = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        secretsStore.RequestedKeys.Count.ShouldBe( 1 );
//        secretsStore.RequestedKeys[0].ShouldContain( "GITLAB_INTERNAL_ORG_GIT_WRITE_PAT" );
//    }

//    #endregion

//    #region Gitea PAT Key Tests

//    [Test]
//    public async System.Threading.Tasks.Task Gitea_uses_sanitized_hostnameAsync()
//    {
//        var secretsStore = new RecordingSecretsStore();
//        using var provider = new GiteaProvider( "gitea.company.com", new System.Uri( "https://gitea.company.com/api/v1/" ), secretsStore );
//        var monitor = new ActivityMonitor();

//        _ = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        secretsStore.RequestedKeys.Count.ShouldBe( 1 );
//        secretsStore.RequestedKeys[0].ShouldContain( "GITEA_COMPANY_COM_GIT_WRITE_PAT" );
//    }

//    [Test]
//    public async System.Threading.Tasks.Task Gitea_with_hyphen_in_hostnameAsync()
//    {
//        var secretsStore = new RecordingSecretsStore();
//        using var provider = new GiteaProvider( "git-server.example.com", new System.Uri( "https://git-server.example.com/api/v1/" ), secretsStore );
//        var monitor = new ActivityMonitor();

//        _ = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        secretsStore.RequestedKeys.Count.ShouldBe( 1 );
//        // Hyphens become underscores
//        secretsStore.RequestedKeys[0].ShouldContain( "GIT_SERVER_EXAMPLE_COM_GIT_WRITE_PAT" );
//    }

//    #endregion

//    #region Hostname Sanitization Edge Cases

//    [Test]
//    public async System.Threading.Tasks.Task Hostname_with_numbers_is_preservedAsync()
//    {
//        var secretsStore = new RecordingSecretsStore();
//        using var provider = new GiteaProvider( "git01.company.com", new System.Uri( "https://git01.company.com/api/v1/" ), secretsStore );
//        var monitor = new ActivityMonitor();

//        _ = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        secretsStore.RequestedKeys.Count.ShouldBe( 1 );
//        secretsStore.RequestedKeys[0].ShouldContain( "GIT01_COMPANY_COM_GIT_WRITE_PAT" );
//    }

//    [Test]
//    public async System.Threading.Tasks.Task Hostname_with_port_should_not_include_portAsync()
//    {
//        // Note: The hostname passed to provider should already have port stripped
//        // This tests that just the hostname is used, not hostname:port
//        var secretsStore = new RecordingSecretsStore();
//        using var provider = new GiteaProvider( "gitea.company.com", new System.Uri( "https://gitea.company.com:8443/api/v1/" ), secretsStore );
//        var monitor = new ActivityMonitor();

//        _ = await provider.GetRepositoryInfoAsync( monitor, "owner", "repo" );

//        secretsStore.RequestedKeys.Count.ShouldBe( 1 );
//        // Should NOT contain port number
//        secretsStore.RequestedKeys[0].ShouldContain( "GITEA_COMPANY_COM_GIT_WRITE_PAT" );
//        secretsStore.RequestedKeys[0].ShouldNotContain( "8443" );
//    }

//    #endregion
//}
