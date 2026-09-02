using CK.Core;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
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
    // The branch the test repositories are created with: GitHub ignores it on creation (its default branch
    // is the account's one) but the first pushed branch must match it for releases to resolve.
    const string DefaultBranchName = "main";

    // Because we are pushing here, we need the Write PAT for the "FILESYSTEM". That is useless
    // (credentials are not used on local file system) but it's good to not make an exception for this case.
    [SetUp]
    public void Setup() => TestEnv.SetFileSystemWritePAT();

    // TearDown is the finally: it runs even when the test fails. The secret must never be left in the
    // store, a test that pushes is the one responsible for registering it.
    [TearDown]
    public void TearDown() => TestEnv.RemoveFileSystemWritePAT();

    [TestCase( "https://github.com/CK-Build/CKli", "GITHUB_CK_BUILD", "CK-Build/Test-Repo-Create", "CK-Build/No Way", true )]
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
        await CreatingAndDeletingReposAsync( p, testRepoName, creds ).ConfigureAwait( false );
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

    static async Task CreatingAndDeletingReposAsync( GitHostingProvider p, string testRepoName, UsernamePasswordCredentials creds )
    {
        // Cleanup any previous run.
        var info = await DeleteTestRepoCreateAsync( p, testRepoName ).ConfigureAwait( false );

        info = await p.CreateRepositoryAsync( TestHelper.Monitor, testRepoName, defaultBranchName: DefaultBranchName ).ConfigureAwait( false );
        info.ShouldNotBeNull();
        info.Exists.ShouldBeTrue();
        info.RepoPath.ShouldBe( testRepoName );
        info.IsArchived.ShouldBeFalse();
        info.CreatedAt.ShouldNotBeNull();
        info.UpdatedAt.ShouldNotBeNull();

        p.IsDefaultPublic.ShouldBeTrue();
        info.IsPrivate.ShouldBe( !p.IsDefaultPublic );

        await TestReleasesAsync( p, testRepoName, info, creds );

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

        static async Task TestReleasesAsync( GitHostingProvider p,
                                             string testRepoName,
                                             HostedRepositoryInfo info,
                                             UsernamePasswordCredentials creds )
        {
            var releases = await p.GetReleaseListAsync( TestHelper.Monitor, testRepoName, 1, 100 );
            releases.ShouldNotBeNull().ShouldBeEmpty();

            // CreateDraftReleaseAsync requires the tag to already be in the remote. A brand new repository
            // has no commit at all: GitHub answers a 422 "Invalid target_commitish parameter".
            PushFirstCommitAndTag( info, creds, DefaultBranchName, "v1.0.0" );

            var releaseId = await p.CreateDraftReleaseAsync( TestHelper.Monitor, testRepoName, "v1.0.0" );
            releaseId.ShouldNotBeNull();
            var filePath = TestHelper.TestProjectFolder.AppendPart( "README.md" );
            (await p.AddReleaseAssetAsync( TestHelper.Monitor, testRepoName, releaseId, filePath, "TestFile.txt" )).ShouldBeTrue();
            releases = await p.GetReleaseListAsync( TestHelper.Monitor, testRepoName, 1, 100 );
            releases.ShouldNotBeNull().Count.ShouldBe( 1 );
            CheckTestRelease( releases[0], releaseId );

            var (success, releaseInfo) = await p.GetReleaseAsync( TestHelper.Monitor, testRepoName, releaseId );
            success.ShouldBeTrue();
            CheckTestRelease( releaseInfo.ShouldNotBeNull(), releaseId );

            (await p.DeleteReleaseAsync( TestHelper.Monitor, testRepoName, releaseId )).ShouldBeTrue();
            // Idempotence.
            (await p.DeleteReleaseAsync( TestHelper.Monitor, testRepoName, releaseId )).ShouldBeTrue();

            (success, releaseInfo) = await p.GetReleaseAsync( TestHelper.Monitor, testRepoName, releaseId );
            success.ShouldBeTrue();
            releaseInfo.ShouldBeNull();

            static void CheckTestRelease( PublishedReleaseInfo i, string expectedReleaseId )
            {
                i.Version.ToString().ShouldBe( "1.0.0" );
                i.Assets.ShouldNotBeEmpty();
                i.Assets[0].ShouldBe( "TestFile.txt" );
                i.ReleaseId.ShouldBe( expectedReleaseId );
            }
        }
    }

    /// <summary>
    /// Clones the (empty) repository, commits a README, tags it and pushes the branch and the tag.
    /// <para>
    /// <see cref="GitHostingProvider.CreateDraftReleaseAsync"/> requires the tag to be in the remote. GitHub
    /// moreover resolves the release against the repository default branch: the local branch (libgit2 names it
    /// after its init.defaultBranch, "master") must be pushed as <paramref name="defaultBranchName"/>, the name
    /// the repository has been created with, otherwise GitHub answers a 422 "Invalid target_commitish parameter".
    /// </para>
    /// </summary>
    static void PushFirstCommitAndTag( HostedRepositoryInfo info,
                                       UsernamePasswordCredentials creds,
                                       string defaultBranchName,
                                       string versionedTag )
    {
        // "file://C:/path" is not a path libgit2 can resolve: the Uri normalizes it to "file:///C:/path".
        var cloneUrl = new Uri( info.CloneUrl.ShouldNotBeNull() ).ToString();
        CredentialsHandler provideCreds = ( _, _, _ ) => creds;
        var workFolder = FileUtil.CreateUniqueTimedFolder( Path.GetTempPath(), "CKli-Test-Release", DateTime.UtcNow );
        try
        {
            var cloneOptions = new CloneOptions();
            cloneOptions.FetchOptions.CredentialsProvider = provideCreds;
            using( var repo = new Repository( Repository.Clone( cloneUrl, workFolder, cloneOptions ) ) )
            {
                File.WriteAllText( Path.Combine( workFolder, "README.md" ), "Created by the CKli CommonProviderTests." );
                Commands.Stage( repo, "*" );
                var signature = new Signature( "CKli", "ckli@invalid.com", DateTimeOffset.Now );
                var commit = repo.Commit( "Initialization.", signature, signature );
                repo.Tags.Add( versionedTag, commit );
                var pushOptions = new PushOptions { CredentialsProvider = provideCreds };
                repo.Network.Push( repo.Network.Remotes["origin"],
                                   [$"refs/heads/{repo.Head.FriendlyName}:refs/heads/{defaultBranchName}",
                                    $"refs/tags/{versionedTag}"],
                                   pushOptions );
            }
        }
        finally
        {
            FileHelper.DeleteFolder( TestHelper.Monitor, workFolder );
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
