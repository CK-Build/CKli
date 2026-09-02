using CK.Core;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
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

    // At least one test here pushes, so the write PAT for the "FILESYSTEM" is required. That is useless
    // (credentials are not used on local file system) but it's good to not make an exception for this case.
    // Once per fixture: registering it is a "dotnet user-secrets" process writing a shared file, doing it
    // around every test is churn on a file that has no transaction.
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    // OneTimeTearDown is the finally: it runs even when a test fails. The secret must never be left in the
    // store, a fixture that pushes is the one responsible for registering it.
    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

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
        if( p.HasDefaultBranch )
        {
            await SettingDefaultBranchAsync( p, testRepoName, creds ).ConfigureAwait( false );
        }
        else
        {
            // The capability is not supported: the call is invalid, not a failure.
            Should.Throw<InvalidOperationException>( () => p.SetDefaultBranchAsync( TestHelper.Monitor, testRepoName, DefaultBranchName ) );
        }
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
        info.DefaultBranch.ShouldBeNull();

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
        if( !p.HasDefaultBranch ) info.DefaultBranch.ShouldBeNull();

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
            PushFirstCommit( info, creds, [DefaultBranchName], "v1.0.0" );

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
    /// Clones the (empty) repository, commits a README, pushes it as each of the <paramref name="branchNames"/>
    /// and, when <paramref name="versionedTag"/> is not null, tags the commit and pushes the tag.
    /// <para>
    /// <see cref="GitHostingProvider.CreateDraftReleaseAsync"/> requires the tag to be in the remote. GitHub
    /// moreover resolves the release against the repository default branch: the local branch (libgit2 names it
    /// after its init.defaultBranch, "master") must be pushed as the first of the <paramref name="branchNames"/>,
    /// the name the repository has been created with, otherwise GitHub answers a 422 "Invalid target_commitish parameter".
    /// </para>
    /// </summary>
    static void PushFirstCommit( HostedRepositoryInfo info,
                                 UsernamePasswordCredentials creds,
                                 string[] branchNames,
                                 string? versionedTag )
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
                var refSpecs = branchNames.Select( b => $"refs/heads/{repo.Head.FriendlyName}:refs/heads/{b}" ).ToList();
                if( versionedTag != null )
                {
                    repo.Tags.Add( versionedTag, commit );
                    refSpecs.Add( $"refs/tags/{versionedTag}" );
                }
                var pushOptions = new PushOptions { CredentialsProvider = provideCreds };
                repo.Network.Push( repo.Network.Remotes["origin"], refSpecs, pushOptions );
            }
        }
        finally
        {
            FileHelper.DeleteFolder( TestHelper.Monitor, workFolder );
        }
    }

    static async Task SettingDefaultBranchAsync( GitHostingProvider p, string testRepoName, UsernamePasswordCredentials creds )
    {
        const string otherBranchName = "some-other-branch";

        // Cleanup any previous run.
        var info = await EnsureDeleteAsync( p, testRepoName ).ConfigureAwait( false );

        info = await p.CreateRepositoryAsync( TestHelper.Monitor, testRepoName, defaultBranchName: DefaultBranchName ).ConfigureAwait( false );
        info.ShouldNotBeNull();
        info.Exists.ShouldBeTrue();

        // A branch can only become the default one once it exists in the remote. Which of the 2 pushed
        // branches the host elects as the default one is not specified: this is what is being set below.
        PushFirstCommit( info, creds, [DefaultBranchName, otherBranchName], versionedTag: null );

        (await p.SetDefaultBranchAsync( TestHelper.Monitor, testRepoName, otherBranchName )).ShouldBeTrue();
        await ShouldEventuallyBeTheDefaultBranchAsync( p, testRepoName, otherBranchName ).ConfigureAwait( false );

        // Idempotence: setting the branch that is already the default one succeeds and changes nothing.
        (await p.SetDefaultBranchAsync( TestHelper.Monitor, testRepoName, otherBranchName )).ShouldBeTrue();
        await ShouldEventuallyBeTheDefaultBranchAsync( p, testRepoName, otherBranchName ).ConfigureAwait( false );

        // Back to the initial branch: the change works in both directions.
        (await p.SetDefaultBranchAsync( TestHelper.Monitor, testRepoName, DefaultBranchName )).ShouldBeTrue();
        await ShouldEventuallyBeTheDefaultBranchAsync( p, testRepoName, DefaultBranchName ).ConfigureAwait( false );

        // A branch that doesn't exist is an error, not a silent success.
        (await p.SetDefaultBranchAsync( TestHelper.Monitor, testRepoName, "no-way-this-branch-exists" )).ShouldBeFalse();
        await ShouldEventuallyBeTheDefaultBranchAsync( p, testRepoName, DefaultBranchName ).ConfigureAwait( false );

        await EnsureDeleteAsync( p, testRepoName ).ConfigureAwait( false );
    }

    /// <summary>
    /// Waits for the repository information to expose <paramref name="branchName"/> as the default branch.
    /// <para>
    /// A read that immediately follows a write can be served a stale repository representation (GitHub answers
    /// the previous default branch for a second or so): asserting on the first read makes the test flaky. The
    /// provider itself never relies on such a read, only this check does.
    /// </para>
    /// </summary>
    static async Task ShouldEventuallyBeTheDefaultBranchAsync( GitHostingProvider p, string testRepoName, string branchName )
    {
        string? read = null;
        for( int i = 0; i < 10; ++i )
        {
            if( i > 0 ) await Task.Delay( 1000 ).ConfigureAwait( false );
            var info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, testRepoName, mustExist: true ).ConfigureAwait( false );
            read = info.ShouldNotBeNull().DefaultBranch;
            if( read == branchName ) return;
            TestHelper.Monitor.Trace( $"Default branch of '{testRepoName}' is still '{read}', waiting for '{branchName}'." );
        }
        read.ShouldBe( branchName, "The default branch didn't become the expected one." );
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
