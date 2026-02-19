using CKli.Core.GitHosting.Providers;
using NUnit.Framework;
using Shouldly;
using System;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests.GitHosting;

[TestFixture]
public class GitHubProviderTests
{
    GitHostingProvider? _gitHubProvider;

    GitHostingProvider GetGitHubCKliProvider()
    {
        if( _gitHubProvider == null )
        {
            // Using the real store here: the PAT must be locally registered for these tests to run.
            var store = new DotNetUserSecretsStore();
            var gitKey = new GitRepositoryKey( store, new Uri( "https://github.com/CK-Build/CKli" ), isPublic: true );
            gitKey.AccessKey.PrefixPAT.ShouldBe( "GITHUB_CK_BUILD" );
            Assume.That( gitKey.AccessKey.GetReadCredentials( TestHelper.Monitor, out var creds ), "The user-secrets store must be configured." );
            _gitHubProvider = new GitHubProvider( gitKey.AccessKey );
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
        info.WebUrl.ShouldBe( "https://github.com/CK-Build/CKli" );
        info.CloneUrl.ShouldBe( "https://github.com/CK-Build/CKli.git" );
        info.CreatedAt.ShouldBe( new DateTime( 2024, 10, 9, 8, 50, 17, DateTimeKind.Utc ) );
        (info.UpdatedAt.ShouldNotBeNull() > info.CreatedAt ).ShouldBeTrue();

        var info2 = await p.GetRepositoryInfoAsync( TestHelper.Monitor, "ck-build/ckli", mustExist: true );
        info2.ShouldBe( info );
    }
}
