using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests.GitHosting;

[TestFixture]
public class CommonProviderTests
{
    [SetUp]
    public void Setup()
    {
        // Because we are pushing here, we need the Write PAT for the "FILESYSTEM"
        // That is useless (credentials are not used on local file system) but it's
        // good to not make an exception for this case.
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  """user-secrets set FILESYSTEM_GIT "don't care" --id CKli-Test""",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );
    }

    // Failing test on Archive with a StatusCode: 422, ReasonPhrase: 'Unprocessable Entity'
    // and no errors details. Giving up.
    // 
    // [TestCase( "https://github.com/CK-Build/CKli", "GITHUB_CK_BUILD", "CK-Build/Test-Repo-Create", "CK-Build/No Way", true )]
    //
    [TestCase( "//Some/path", "FILESYSTEM_GIT", "{TempPath}/CKli-Test/Test-Repo-Create", "A/path/That/Doesn't/Exist", true )]
    public async Task common_API_test_Async( string keyRepositoryUrl,
                                             string expectedPrefixPAT,
                                             string testRepoName,
                                             string unexistingRepoName,
                                             bool isPublic )
    {
        testRepoName = testRepoName.Replace( "{TempPath}", Path.GetTempPath() );
        // Using the real store here: the PAT must be locally registered for these tests to run.
        var store = new DotNetUserSecretsStore();
        var gitKey = new GitRepositoryKey( store, new Uri( keyRepositoryUrl ), isPublic );
        gitKey.AccessKey.PrefixPAT.ShouldBe( expectedPrefixPAT );

        Assume.That( gitKey.AccessKey.GetWriteCredentials( TestHelper.Monitor, out var creds ),
                     "The user-secrets store must be configured." );

        var p = gitKey.AccessKey.HostingProvider;
        p.ShouldNotBeNull();
        await GetUnexistingRepoInfoAsync( p, unexistingRepoName ).ConfigureAwait( false );
        await CreatingAndDeletingReposAsync( p, testRepoName ).ConfigureAwait( false );
        if( p.CanArchiveRepository )
        {
            await ArchivingReposAsync( p, testRepoName ).ConfigureAwait( false );
        }
    }

    static async Task GetUnexistingRepoInfoAsync( GitHostingProvider p, string unexistingRepoName )
    {
        var info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, unexistingRepoName, mustExist: false ).ConfigureAwait( false );
        info.ShouldNotBeNull();
        info.Exists.ShouldBeFalse();
        info.RepoPath.IsEmptyPath.ShouldBeTrue();
        info.IsArchived.ShouldBeFalse();
        info.IsPrivate.ShouldBeFalse();
        info.WebUrl.ShouldBeNull();
        info.CloneUrl.ShouldBeNull();
        info.CreatedAt.ShouldBeNull();
        info.UpdatedAt.ShouldBeNull();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, unexistingRepoName, mustExist: true ).ConfigureAwait( false );
            info.ShouldBeNull();
            logs.ShouldContain( l => Regex.IsMatch( l, $"Expected Git repository at '{p.BaseUrl}.*{unexistingRepoName}' is missing\\." ) );
        }
    }

    static async Task CreatingAndDeletingReposAsync( GitHostingProvider p, string testRepoName )
    {
        // Cleanup any previous run.
        var info = await DeleteTestRepoCreateAsync( p, testRepoName ).ConfigureAwait( false );

        info = await p.CreateRepositoryAsync( TestHelper.Monitor, testRepoName ).ConfigureAwait( false );
        info.ShouldNotBeNull();
        info.Exists.ShouldBeTrue();
        info.RepoPath.ShouldBe( testRepoName );
        info.IsArchived.ShouldBeFalse();
        info.CreatedAt.ShouldNotBeNull();
        info.UpdatedAt.ShouldNotBeNull();

        p.IsDefaultPublic.ShouldBeTrue();
        info.IsPrivate.ShouldBe( !p.IsDefaultPublic );

        await DeleteTestRepoCreateAsync( p, testRepoName ).ConfigureAwait( false );

        // Creating a private (or public) repository.
        if( p.GitKey.IsPublic is not null )
        {
            info = await p.CreateRepositoryAsync( TestHelper.Monitor, testRepoName, isPrivate: p.IsDefaultPublic ).ConfigureAwait( false );
            info.ShouldNotBeNull();
            info.Exists.ShouldBeTrue();
            info.RepoPath.ShouldBe( testRepoName );
            info.IsPrivate.ShouldBe( p.IsDefaultPublic );
            info.IsArchived.ShouldBeFalse();
            info.CreatedAt.ShouldNotBeNull();
            info.UpdatedAt.ShouldNotBeNull();
        }

        static async Task<HostedRepositoryInfo> DeleteTestRepoCreateAsync( GitHostingProvider p, string testRepoName )
        {
            var info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, testRepoName, mustExist: false ).ConfigureAwait( false );
            info.ShouldNotBeNull();
            if( info.Exists )
            {
                (await p.DeleteRepositoryAsync( TestHelper.Monitor, testRepoName ).ConfigureAwait( false )).ShouldBeTrue();
                info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, testRepoName, mustExist: false ).ConfigureAwait( false );
                info.ShouldNotBeNull();
                info.Exists.ShouldBeFalse();
            }
            return info;
        }
    }
    static async Task ArchivingReposAsync( GitHostingProvider p, string testRepoName )
    {
        // Cleanup any previous run.
        var info = await EnsureDeleteAsync( p, testRepoName ).ConfigureAwait( false );

        info = await p.CreateRepositoryAsync( TestHelper.Monitor, testRepoName ).ConfigureAwait( false );
        info.ShouldNotBeNull();
        info.Exists.ShouldBeTrue();

        (await p.ArchiveRepositoryAsync( TestHelper.Monitor, testRepoName, archive: true )).ShouldBeTrue();

        info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, testRepoName, mustExist: true ).ConfigureAwait( false );
        info.ShouldNotBeNull();
        info.Exists.ShouldBeTrue();
        info.IsArchived.ShouldBeTrue(); 

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await p.ArchiveRepositoryAsync( TestHelper.Monitor, testRepoName, archive: true )).ShouldBeTrue();
            logs.ShouldContain( l => Regex.IsMatch( l, $"Repository '{p.BaseUrl}.*{testRepoName}' is already archived\\." ) );
        }

        await EnsureDeleteAsync( p, testRepoName ).ConfigureAwait( false );
    }

    static async Task<HostedRepositoryInfo> EnsureDeleteAsync( GitHostingProvider p, string testRepoName )
    {
        var info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, testRepoName, mustExist: false ).ConfigureAwait( false );
        info.ShouldNotBeNull();
        if( info.Exists )
        {
            (await p.DeleteRepositoryAsync( TestHelper.Monitor, testRepoName ).ConfigureAwait( false )).ShouldBeTrue();
            info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, testRepoName, mustExist: false ).ConfigureAwait( false );
            info.ShouldNotBeNull();
            info.Exists.ShouldBeFalse();
        }
        return info;
    }
}
