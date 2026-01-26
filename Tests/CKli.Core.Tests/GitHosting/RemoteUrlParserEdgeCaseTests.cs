using CKli.Core.GitHosting;
using NUnit.Framework;
using Shouldly;

namespace CKli.Core.Tests.GitHosting;

/// <summary>
/// Edge case tests for RemoteUrlParser that cover unusual but valid URL formats.
/// </summary>
[TestFixture]
public class RemoteUrlParserEdgeCaseTests
{
    #region URLs with Ports

    [TestCase( "https://github.company.com:8443/owner/repo", "github.company.com" )]
    [TestCase( "https://gitlab.internal:443/group/repo", "gitlab.internal" )]
    [TestCase( "ssh://git@gitea.company.com:2222/owner/repo.git", "gitea.company.com" )]
    public void GetHost_strips_port_numbers( string input, string expectedHost )
    {
        var result = RemoteUrlParser.GetHost( input );
        result.ShouldBe( expectedHost );
    }

    [Test]
    public void TryNormalizeToHttps_preserves_https_url_but_strips_nothing_about_port()
    {
        // Note: The implementation preserves the port in HTTPS URLs
        var result = RemoteUrlParser.TryNormalizeToHttps( "https://github.company.com:8443/owner/repo" );
        result.ShouldNotBeNull();
        result!.Host.ShouldBe( "github.company.com" );
        result.Port.ShouldBe( 8443 );
    }

    #endregion

    #region SSH URLs with Custom Ports

    [Test]
    public void TryNormalizeToHttps_ssh_scheme_with_port_extracts_host_correctly()
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( "ssh://git@gitlab.company.com:2222/group/repo.git" );
        result.ShouldNotBeNull();
        result!.Host.ShouldBe( "gitlab.company.com" );
        // HTTPS normalized URL should not include the SSH port
        result.Port.ShouldBe( 443 ); // Default HTTPS port
    }

    #endregion

    #region Deeply Nested GitLab Groups

    [TestCase( "a/b/c/d/repo", "a/b/c/d", "repo" )]
    [TestCase( "/org/team/subteam/project/repo.git", "org/team/subteam/project", "repo" )]
    public void ParseStandardPath_handles_deeply_nested_paths( string input, string expectedOwner, string expectedRepo )
    {
        var result = RemoteUrlParser.ParseStandardPath( input );
        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( expectedOwner );
        result.Value.RepoName.ShouldBe( expectedRepo );
    }

    #endregion

    #region Edge Cases in Repository Names

    [TestCase( "owner/repo-name", "owner", "repo-name" )]
    [TestCase( "owner/repo_name", "owner", "repo_name" )]
    [TestCase( "owner/repo.name", "owner", "repo.name" )]
    [TestCase( "owner/REPO", "owner", "REPO" )]
    [TestCase( "owner/123", "owner", "123" )]
    public void ParseStandardPath_handles_special_repo_names( string input, string expectedOwner, string expectedRepo )
    {
        var result = RemoteUrlParser.ParseStandardPath( input );
        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( expectedOwner );
        result.Value.RepoName.ShouldBe( expectedRepo );
    }

    #endregion

    #region Trailing Slashes and Whitespace

    [TestCase( "  https://github.com/owner/repo  ", "https://github.com/owner/repo" )]
    [TestCase( "https://github.com/owner/repo/", "https://github.com/owner/repo" )]
    [TestCase( "https://github.com/owner/repo//", "https://github.com/owner/repo" )]
    public void TryNormalizeToHttps_handles_whitespace_and_trailing_slashes( string input, string expected )
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( input );
        result.ShouldNotBeNull();
        result!.ToString().ShouldBe( expected );
    }

    #endregion

    #region Case Sensitivity

    [Test]
    public void GetHost_lowercases_hostname()
    {
        // Uri normalizes hostnames to lowercase per RFC 3986
        var result = RemoteUrlParser.GetHost( "https://GitHub.COM/Owner/Repo" );
        result.ShouldBe( "github.com" );
    }

    [Test]
    public void ParseStandardPath_preserves_case()
    {
        var result = RemoteUrlParser.ParseStandardPath( "Owner/RepoName" );
        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( "Owner" );
        result.Value.RepoName.ShouldBe( "RepoName" );
    }

    #endregion

    #region Invalid Inputs

    [TestCase( "justaword" )]
    [TestCase( "no-slash-here" )]
    [TestCase( "@#$%^&*()" )]
    public void GetHost_returns_null_for_invalid_formats( string input )
    {
        var result = RemoteUrlParser.GetHost( input );
        result.ShouldBeNull();
    }

    [TestCase( null )]
    [TestCase( "" )]
    [TestCase( "   " )]
    public void TryNormalizeToHttps_returns_null_for_empty_input( string? input )
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( input! );
        result.ShouldBeNull();
    }

    #endregion

    #region HTTP to HTTPS Upgrade

    [Test]
    public void TryNormalizeToHttps_upgrades_http_to_https()
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( "http://github.com/owner/repo" );
        result.ShouldNotBeNull();
        result!.Scheme.ShouldBe( "https" );
        result.Host.ShouldBe( "github.com" );
    }

    [Test]
    public void TryNormalizeToHttps_preserves_path_when_upgrading_http()
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( "http://gitlab.com/group/subgroup/repo" );
        result.ShouldNotBeNull();
        result!.AbsolutePath.ShouldBe( "/group/subgroup/repo" );
    }

    #endregion

    #region Single Segment and Edge Case Paths

    [TestCase( "/repo" )]
    [TestCase( "repo" )]
    public void ParseStandardPath_returns_null_for_single_segment( string input )
    {
        var result = RemoteUrlParser.ParseStandardPath( input );
        result.ShouldBeNull();
    }

    [Test]
    public void ParseStandardPath_handles_empty_string()
    {
        var result = RemoteUrlParser.ParseStandardPath( "" );
        result.ShouldBeNull();
    }

    [Test]
    public void ParseStandardPath_handles_only_slashes()
    {
        var result = RemoteUrlParser.ParseStandardPath( "///" );
        result.ShouldBeNull();
    }

    [Test]
    public void ParseStandardPath_handles_path_with_only_owner()
    {
        // Path like "/owner/" should return null (no repo name)
        var result = RemoteUrlParser.ParseStandardPath( "/owner/" );
        result.ShouldBeNull();
    }

    #endregion

    #region Scheme-less URLs

    [Test]
    public void TryNormalizeToHttps_handles_schemeless_url()
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( "github.com/owner/repo" );
        result.ShouldNotBeNull();
        result!.Scheme.ShouldBe( "https" );
        result.Host.ShouldBe( "github.com" );
    }

    [Test]
    public void TryNormalizeToHttps_handles_schemeless_url_with_git_suffix()
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( "github.com/owner/repo.git" );
        result.ShouldNotBeNull();
        result!.AbsolutePath.ShouldBe( "/owner/repo" );
    }

    #endregion

    #region SSH SCP Format Variations

    [TestCase( "git@github.com:owner/repo", "github.com", "owner", "repo" )]
    [TestCase( "git@github.com:owner/repo.git", "github.com", "owner", "repo" )]
    [TestCase( "git@gitlab.com:group/subgroup/repo.git", "gitlab.com", "group/subgroup", "repo" )]
    [TestCase( "deploy@gitea.company.com:team/project.git", "gitea.company.com", "team", "project" )]
    [TestCase( "user@private-git.internal:org/repo", "private-git.internal", "org", "repo" )]
    public void SshScpFormat_parses_correctly( string input, string expectedHost, string expectedOwner, string expectedRepo )
    {
        var host = RemoteUrlParser.GetHost( input );
        host.ShouldBe( expectedHost );

        var normalized = RemoteUrlParser.TryNormalizeToHttps( input );
        normalized.ShouldNotBeNull();

        var parsed = RemoteUrlParser.ParseStandardPath( normalized!.AbsolutePath );
        parsed.ShouldNotBeNull();
        parsed!.Value.Owner.ShouldBe( expectedOwner );
        parsed.Value.RepoName.ShouldBe( expectedRepo );
    }

    #endregion
}
