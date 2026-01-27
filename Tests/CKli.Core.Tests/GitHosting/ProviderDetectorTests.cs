using CK.Core;
using CKli.Core.GitHosting;
using CKli.Core.GitHosting.Providers;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;

namespace CKli.Core.Tests.GitHosting;

[TestFixture]
public class ProviderDetectorTests
{
    // A mock secrets store that returns null for all secrets (no credentials configured)
    sealed class NoCredentialsStore : ISecretsStore
    {
        public string? TryGetRequiredSecret( IActivityMonitor monitor, System.Collections.Generic.IEnumerable<string> keys ) => null;
    }

    readonly ISecretsStore _noCredentials = new NoCredentialsStore();

    #region CreateProvider Tests (Direct Creation)

    [Test]
    public void CreateProvider_github_creates_GitHubProvider_for_github_com()
    {
        using var provider = ProviderDetector.CreateProvider( "github", "github.com", _noCredentials );

        provider.ShouldNotBeNull();
        provider.ShouldBeOfType<GitHubProvider>();
        provider!.InstanceId.ShouldBe( "github.com" );
        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.GitHub );
        provider.BaseApiUrl.ToString().ShouldBe( "https://api.github.com/" ); // Trailing slash required
    }

    [Test]
    public void CreateProvider_github_creates_GitHubProvider_for_enterprise()
    {
        using var provider = ProviderDetector.CreateProvider( "github", "github.mycompany.com", _noCredentials );

        provider.ShouldNotBeNull();
        provider.ShouldBeOfType<GitHubProvider>();
        provider!.InstanceId.ShouldBe( "github.mycompany.com" );
        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.Unknown );
        provider.BaseApiUrl.ToString().ShouldBe( "https://github.mycompany.com/api/v3/" );
    }

    [Test]
    public void CreateProvider_gitlab_creates_GitLabProvider_for_gitlab_com()
    {
        using var provider = ProviderDetector.CreateProvider( "gitlab", "gitlab.com", _noCredentials );

        provider.ShouldNotBeNull();
        provider.ShouldBeOfType<GitLabProvider>();
        provider!.InstanceId.ShouldBe( "gitlab.com" );
        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.GitLab );
        provider.BaseApiUrl.ToString().ShouldBe( "https://gitlab.com/api/v4/" );
    }

    [Test]
    public void CreateProvider_gitlab_creates_GitLabProvider_for_self_hosted()
    {
        using var provider = ProviderDetector.CreateProvider( "gitlab", "gitlab.internal.org", _noCredentials );

        provider.ShouldNotBeNull();
        provider.ShouldBeOfType<GitLabProvider>();
        provider!.InstanceId.ShouldBe( "gitlab.internal.org" );
        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.Unknown );
        provider.BaseApiUrl.ToString().ShouldBe( "https://gitlab.internal.org/api/v4/" );
    }

    [Test]
    public void CreateProvider_gitea_creates_GiteaProvider()
    {
        using var provider = ProviderDetector.CreateProvider( "gitea", "gitea.company.com", _noCredentials );

        provider.ShouldNotBeNull();
        provider.ShouldBeOfType<GiteaProvider>();
        provider!.InstanceId.ShouldBe( "gitea.company.com" );
        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.Unknown ); // Gitea is always Unknown
        provider.BaseApiUrl.ToString().ShouldBe( "https://gitea.company.com/api/v1/" );
    }

    [Test]
    public void CreateProvider_returns_null_for_unknown_type()
    {
        var provider = ProviderDetector.CreateProvider( "unknown", "some.host.com", _noCredentials );
        provider.ShouldBeNull();
    }

    [Test]
    public void CreateProvider_is_case_insensitive()
    {
        using var provider1 = ProviderDetector.CreateProvider( "GITHUB", "github.com", _noCredentials );
        using var provider2 = ProviderDetector.CreateProvider( "GitHub", "github.com", _noCredentials );
        using var provider3 = ProviderDetector.CreateProvider( "gitHub", "github.com", _noCredentials );

        provider1.ShouldNotBeNull();
        provider2.ShouldNotBeNull();
        provider3.ShouldNotBeNull();
    }

    #endregion

    #region Provider URL Parsing Tests

    [Test]
    public void GitHubProvider_parses_https_url()
    {
        using var provider = ProviderDetector.CreateProvider( "github", "github.com", _noCredentials );

        var result = provider!.ParseRemoteUrl( "https://github.com/owner/repo" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "owner" );
        result!.Value.RepoName.ShouldBe( "repo" );
    }

    [Test]
    public void GitHubProvider_parses_ssh_url()
    {
        using var provider = ProviderDetector.CreateProvider( "github", "github.com", _noCredentials );

        var result = provider!.ParseRemoteUrl( "git@github.com:owner/repo.git" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "owner" );
        result!.Value.RepoName.ShouldBe( "repo" );
    }

    [Test]
    public void GitHubProvider_returns_null_for_different_host()
    {
        using var provider = ProviderDetector.CreateProvider( "github", "github.com", _noCredentials );

        var result = provider!.ParseRemoteUrl( "https://gitlab.com/owner/repo" );

        result.ShouldBeNull();
    }

    [Test]
    public void GitLabProvider_parses_nested_groups()
    {
        using var provider = ProviderDetector.CreateProvider( "gitlab", "gitlab.com", _noCredentials );

        var result = provider!.ParseRemoteUrl( "https://gitlab.com/group/subgroup/repo" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "group/subgroup" );
        result!.Value.RepoName.ShouldBe( "repo" );
    }

    [Test]
    public void GiteaProvider_parses_url_for_self_hosted()
    {
        using var provider = ProviderDetector.CreateProvider( "gitea", "gitea.company.com", _noCredentials );

        var result = provider!.ParseRemoteUrl( "https://gitea.company.com/team/project" );

        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "team" );
        result!.Value.RepoName.ShouldBe( "project" );
    }

    #endregion

    #region ResolveProviderAsync Tests (Well-Known and Pattern Detection)

    [Test]
    public async Task ResolveProviderAsync_detects_github_comAsync()
    {
        var monitor = new ActivityMonitor();

        using var provider = await ProviderDetector.ResolveProviderAsync(
            monitor,
            _noCredentials,
            "https://github.com/owner/repo" );

        provider.ShouldNotBeNull();
        provider.ShouldBeOfType<GitHubProvider>();
        provider!.InstanceId.ShouldBe( "github.com" );
        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.GitHub );
    }

    [Test]
    public async Task ResolveProviderAsync_detects_gitlab_comAsync()
    {
        var monitor = new ActivityMonitor();

        using var provider = await ProviderDetector.ResolveProviderAsync(
            monitor,
            _noCredentials,
            "https://gitlab.com/group/repo" );

        provider.ShouldNotBeNull();
        provider.ShouldBeOfType<GitLabProvider>();
        provider!.InstanceId.ShouldBe( "gitlab.com" );
        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.GitLab );
    }

    [Test]
    public async Task ResolveProviderAsync_detects_gitea_from_hostname_patternAsync()
    {
        var monitor = new ActivityMonitor();

        // Gitea detection via hostname pattern (contains "gitea")
        using var provider = await ProviderDetector.ResolveProviderAsync(
            monitor,
            _noCredentials,
            "https://gitea.mycompany.com/owner/repo" );

        provider.ShouldNotBeNull();
        provider.ShouldBeOfType<GiteaProvider>();
        provider!.InstanceId.ShouldBe( "gitea.mycompany.com" );
    }

    [Test]
    public async Task ResolveProviderAsync_detects_gitlab_from_hostname_patternAsync()
    {
        var monitor = new ActivityMonitor();

        // GitLab detection via hostname pattern (contains "gitlab")
        using var provider = await ProviderDetector.ResolveProviderAsync(
            monitor,
            _noCredentials,
            "https://gitlab.internal.org/group/repo" );

        provider.ShouldNotBeNull();
        provider.ShouldBeOfType<GitLabProvider>();
        provider!.InstanceId.ShouldBe( "gitlab.internal.org" );
    }

    [Test]
    public async Task ResolveProviderAsync_detects_github_enterprise_from_hostname_patternAsync()
    {
        var monitor = new ActivityMonitor();

        // GitHub Enterprise detection via hostname pattern (contains "github")
        using var provider = await ProviderDetector.ResolveProviderAsync(
            monitor,
            _noCredentials,
            "https://github.enterprise.com/org/repo" );

        provider.ShouldNotBeNull();
        provider.ShouldBeOfType<GitHubProvider>();
        provider!.InstanceId.ShouldBe( "github.enterprise.com" );
        provider.CloudProvider.ShouldBe( KnownCloudGitProvider.Unknown ); // Enterprise = Unknown
    }

    [Test]
    public async Task ResolveProviderAsync_handles_ssh_urlsAsync()
    {
        var monitor = new ActivityMonitor();

        using var provider = await ProviderDetector.ResolveProviderAsync(
            monitor,
            _noCredentials,
            "git@github.com:owner/repo.git" );

        provider.ShouldNotBeNull();
        provider.ShouldBeOfType<GitHubProvider>();
    }

    [Test]
    public async Task ResolveProviderAsync_returns_null_for_invalid_urlAsync()
    {
        var monitor = new ActivityMonitor();

        var provider = await ProviderDetector.ResolveProviderAsync(
            monitor,
            _noCredentials,
            "not-a-valid-url" );

        provider.ShouldBeNull();
    }

    #endregion
}
