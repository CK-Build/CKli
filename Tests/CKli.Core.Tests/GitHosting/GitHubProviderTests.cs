using CKli.Core.GitHosting.Providers;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests.GitHosting;

[TestFixture]
public class GitHubProviderTests
{
    HttpGitHostingProvider? _gitHubProvider;

    GitHostingProvider GetGitHubCKliProvider()
    {
        if( _gitHubProvider == null )
        {
            // Using the real store here: the PAT must be locally registered for these tests to run.
            var store = new DotNetUserSecretsStore();
            // The CKli repository is public.
            // The hosting provider is public (new repositories will be public bu default).
            // But, to call GitHub, it is better to always use a PAT because anonymous API calls
            // have a low rate limit: AlwaysUseAuthentication is true for GitHub.
            // => To challenge the Read credentials, we must actually consider the ToPrivateAccessKey() instance.
            var gitKey = new GitRepositoryKey( store, new Uri( "https://github.com/CK-Build/CKli" ), isPublic: true );
            gitKey.AccessKey.PrefixPAT.ShouldBe( "GITHUB_CK_BUILD" );
            _gitHubProvider = (HttpGitHostingProvider?)gitKey.AccessKey.HostingProvider;
            _gitHubProvider.ShouldNotBeNull();
            _gitHubProvider.AlwaysUseAuthentication.ShouldBeTrue();
            Assume.That( _gitHubProvider.GitKey.ToPrivateAccessKey().GetReadCredentials( TestHelper.Monitor, out var creds ),
                         "The user-secrets store must be configured." );
        }
        return _gitHubProvider;
    }

    [Test]
    public async Task get_CKli_info_Async()
    {
        var p = GetGitHubCKliProvider();

        var info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, "CK-Build/CKli", mustExist: true );
        info.ShouldNotBeNull();
        info.Exists.ShouldBeTrue();
        info.RepoPath.ShouldBe( "CK-Build/CKli" );
        info.IsArchived.ShouldBeFalse();
        info.IsPrivate.ShouldBeFalse();
        p.HasDefaultBranch.ShouldBeTrue();
        info.DefaultBranch.ShouldBe( "stable" );
        info.WebUrl.ShouldBe( "https://github.com/CK-Build/CKli" );
        info.CloneUrl.ShouldBe( "https://github.com/CK-Build/CKli.git" );
        info.CreatedAt.ShouldBe( new DateTime( 2024, 10, 9, 8, 50, 17, DateTimeKind.Utc ) );
        (info.UpdatedAt.ShouldNotBeNull() > info.CreatedAt ).ShouldBeTrue();

        var info2 = await p.GetRepositoryInfoAsync( TestHelper.Monitor, "ck-build/ckli", mustExist: true );
        info2.ShouldBe( info );
    }

    [Test]
    public async Task get_CKli_ReleaseList_Async()
    {
        var p = GetGitHubCKliProvider();

        var releases = await p.GetReleaseListAsync( TestHelper.Monitor, "CK-Build/CKli", 1, 100 );
        releases.ShouldNotBeNull().ShouldNotBeEmpty();
        var v = releases.Single( i => i.Version.ToString() == "0.10.3" );
        v.CreatedAt.ShouldBe( DateTime.Parse( "2026/06/29T09:11:04" ) );
        v.IsPublished.ShouldBeTrue();
        v.Assets.ShouldBeEmpty();
    }
}
