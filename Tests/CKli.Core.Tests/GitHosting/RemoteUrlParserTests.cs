using CKli.Core.GitHosting;
using NUnit.Framework;
using Shouldly;
using System;

namespace CKli.Core.Tests.GitHosting;

[TestFixture]
public class RemoteUrlParserTests
{
    #region TryNormalizeToHttps Tests

    [TestCase( "https://github.com/owner/repo", "https://github.com/owner/repo" )]
    [TestCase( "https://github.com/owner/repo.git", "https://github.com/owner/repo.git" )] // .git preserved for HTTPS
    [TestCase( "https://gitlab.com/group/subgroup/repo", "https://gitlab.com/group/subgroup/repo" )]
    [TestCase( "https://gitlab.com/group/subgroup/repo.git", "https://gitlab.com/group/subgroup/repo.git" )] // .git preserved
    [TestCase( "http://github.com/owner/repo", "https://github.com/owner/repo" )]
    [TestCase( "http://github.com/owner/repo.git", "https://github.com/owner/repo.git" )] // .git preserved for HTTP
    public void TryNormalizeToHttps_handles_https_and_http_urls( string input, string expected )
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( input );
        result.ShouldNotBeNull();
        result!.ToString().ShouldBe( expected );
    }

    [TestCase( "git@github.com:owner/repo.git", "https://github.com/owner/repo" )]
    [TestCase( "git@github.com:owner/repo", "https://github.com/owner/repo" )]
    [TestCase( "git@gitlab.com:group/subgroup/repo.git", "https://gitlab.com/group/subgroup/repo" )]
    [TestCase( "git@gitea.company.com:owner/repo.git", "https://gitea.company.com/owner/repo" )]
    public void TryNormalizeToHttps_handles_ssh_scp_format( string input, string expected )
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( input );
        result.ShouldNotBeNull();
        result!.ToString().ShouldBe( expected );
    }

    [TestCase( "ssh://git@github.com/owner/repo.git", "https://github.com/owner/repo" )]
    [TestCase( "ssh://git@github.com/owner/repo", "https://github.com/owner/repo" )]
    [TestCase( "ssh://git@gitlab.com:22/group/repo.git", "https://gitlab.com/group/repo" )]
    [TestCase( "ssh://user@gitea.company.com:2222/owner/repo.git", "https://gitea.company.com/owner/repo" )]
    public void TryNormalizeToHttps_handles_ssh_scheme_format( string input, string expected )
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( input );
        result.ShouldNotBeNull();
        result!.ToString().ShouldBe( expected );
    }

    [TestCase( "github.com/owner/repo", "https://github.com/owner/repo" )]
    [TestCase( "github.com/owner/repo.git", "https://github.com/owner/repo" )]
    [TestCase( "gitlab.com/group/repo/", "https://gitlab.com/group/repo" )]
    public void TryNormalizeToHttps_handles_schemeless_urls( string input, string expected )
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( input );
        result.ShouldNotBeNull();
        result!.ToString().ShouldBe( expected );
    }

    [TestCase( null )]
    [TestCase( "" )]
    [TestCase( "   " )]
    [TestCase( "not-a-url" )]
    public void TryNormalizeToHttps_returns_null_for_invalid_input( string? input )
    {
        var result = RemoteUrlParser.TryNormalizeToHttps( input! );
        result.ShouldBeNull();
    }

    #endregion

    #region GetHost Tests

    [TestCase( "https://github.com/owner/repo", "github.com" )]
    [TestCase( "https://gitlab.com/group/repo", "gitlab.com" )]
    [TestCase( "https://gitea.company.com/owner/repo", "gitea.company.com" )]
    [TestCase( "git@github.com:owner/repo.git", "github.com" )]
    [TestCase( "git@gitlab.myorg.local:group/repo.git", "gitlab.myorg.local" )]
    [TestCase( "ssh://git@github.com/owner/repo", "github.com" )]
    [TestCase( "ssh://git@gitea.company.com:2222/owner/repo", "gitea.company.com" )]
    public void GetHost_extracts_host_correctly( string input, string expected )
    {
        var result = RemoteUrlParser.GetHost( input );
        result.ShouldBe( expected );
    }

    [TestCase( null )]
    [TestCase( "" )]
    [TestCase( "   " )]
    public void GetHost_returns_null_for_invalid_input( string? input )
    {
        var result = RemoteUrlParser.GetHost( input! );
        result.ShouldBeNull();
    }

    #endregion

    #region ParseStandardPath Tests

    [TestCase( "owner/repo", "owner", "repo" )]
    [TestCase( "/owner/repo", "owner", "repo" )]
    [TestCase( "/owner/repo/", "owner", "repo" )]
    [TestCase( "owner/repo.git", "owner", "repo" )]
    [TestCase( "/owner/repo.git", "owner", "repo" )]
    public void ParseStandardPath_handles_simple_paths( string input, string expectedOwner, string expectedRepo )
    {
        var result = RemoteUrlParser.ParseStandardPath( input );
        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( expectedOwner );
        result!.Value.RepoName.ShouldBe( expectedRepo );
    }

    [TestCase( "group/subgroup/repo", "group/subgroup", "repo" )]
    [TestCase( "/group/subgroup/repo", "group/subgroup", "repo" )]
    [TestCase( "org/team/project/repo.git", "org/team/project", "repo" )]
    public void ParseStandardPath_handles_nested_paths( string input, string expectedOwner, string expectedRepo )
    {
        var result = RemoteUrlParser.ParseStandardPath( input );
        result.ShouldNotBeNull();
        result!.Value.Owner.ShouldBe( expectedOwner );
        result!.Value.RepoName.ShouldBe( expectedRepo );
    }

    [TestCase( null )]
    [TestCase( "" )]
    [TestCase( "   " )]
    [TestCase( "repo" )]
    [TestCase( "/" )]
    public void ParseStandardPath_returns_null_for_invalid_input( string? input )
    {
        var result = RemoteUrlParser.ParseStandardPath( input! );
        result.ShouldBeNull();
    }

    #endregion
}
