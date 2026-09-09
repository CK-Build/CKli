using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;
// LibGit2Sharp has a LogLevel too.
using LogLevel = CK.Core.LogLevel;

namespace CKli.Core.Tests.GitHosting;

[TestFixture]
public class FileSystemProviderTests
{

    /// <summary>
    /// Gets the shared file system hosting provider. Access keys are indexed by store and prefix, so the
    /// <paramref name="secretsStore"/> must be provided to obtain the same instance as another key.
    /// </summary>
    public static GitHostingProvider GetFileHostingProvider( ISecretsStore? secretsStore = null )
    {
        secretsStore ??= new RecordingSecretsStore();
        var key = new GitRepositoryKey( secretsStore, new Uri( "C:/Some/path" ), isPublic: true );
        var p = key.AccessKey.HostingProvider;
        return p.ShouldNotBeNull();
    }

    [Test]
    public async Task one_instance_for_public_or_private_from_any_file_scheme_Async()
    {
        var secretsStore = new RecordingSecretsStore();

        var p1 = GetFileHostingProvider( secretsStore );
        p1.ProviderType.ShouldBe( "FileSystemProvider" );

        var gitKey2 = new GitRepositoryKey( secretsStore, new Uri( "//Some/path" ), isPublic: true );
        var p2 = gitKey2.AccessKey.HostingProvider;
        p2.ShouldNotBeNull().ProviderType.ShouldBe( "FileSystemProvider" );

        p1.ShouldBeSameAs( p2 );
        p1.IsDefaultPublic.ShouldBeTrue();
        p1.BaseUrl.ToString().ShouldBe( "file://" );

        var privKey1 = new GitRepositoryKey( secretsStore, new Uri( "//Some/path" ), isPublic: false );
        var priv1 = privKey1.AccessKey.HostingProvider;
        priv1.ShouldNotBeNull().ProviderType.ShouldBe( "FileSystemProvider" );

        priv1.ShouldBeSameAs( p1 );

        var privKey2 = new GitRepositoryKey( secretsStore, new Uri( "X:\\Another" ), isPublic: false );
        var priv2 = privKey2.AccessKey.HostingProvider;
        priv2.ShouldNotBeNull().ProviderType.ShouldBe( "FileSystemProvider" );

        priv2.ShouldBeSameAs( priv1 );
        priv1.IsDefaultPublic.ShouldBeTrue();
    }

    [Test]
    public async Task there_is_no_default_branch_Async()
    {
        var p = GetFileHostingProvider();
        p.HasDefaultBranch.ShouldBeFalse();

        // An existing bare repository has a HEAD but the provider exposes no default branch.
        var bareCKtStack = TestHelper.TestProjectFolder.Combine( "Remotes/bare/CKt/CKt-Stack" );
        var info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, bareCKtStack, mustExist: true );
        info.ShouldNotBeNull().DefaultBranch.ShouldBeNull();

        // Calling SetDefaultBranchAsync is invalid, it is not a failure: the exception is
        // synchronous (it is not the returned task that faults).
        Should.Throw<InvalidOperationException>( () => p.SetDefaultBranchAsync( TestHelper.Monitor, bareCKtStack, "some-branch" ) );
    }

    [Test]
    public async Task info_on_non_existing_git_or_non_bare_repo_is_an_error_Async()
    {
        var p = GetFileHostingProvider();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, TestHelper.TestProjectFolder.AppendPart( "No way" ), mustExist: true );
            info.ShouldBeNull();
            logs.ShouldContain( l => Regex.IsMatch( l, @"Expected Git repository at 'file://.*CKli/Tests/CKli\.Core\.Tests/No way' is missing\." ) );

            info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, TestHelper.TestProjectFolder.AppendPart( "No way" ), mustExist: false );
            info.ShouldNotBeNull();
            info.Exists.ShouldBeFalse();
        }

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, TestHelper.SolutionFolder, mustExist: true );
            info.ShouldBeNull();
            logs.ShouldContain( l => Regex.IsMatch( l.Replace( '\\', '/' ),
                                                    @"Expected bare \.git repository at '.*/CKli'\." ) );
        }
    }

    [Test]
    public async Task reading_a_file_from_a_bare_repo_Async()
    {
        var p = GetFileHostingProvider();
        // OpenRemotes re-extracts the bare repositories: this test doesn't depend on what another one left.
        TestEnv.OpenRemotes( "CKt" );
        var bareCKtStack = TestHelper.TestProjectFolder.Combine( "Remotes/bare/CKt/CKt-Stack" );
        var bareCKtCore = TestHelper.TestProjectFolder.Combine( "Remotes/bare/CKt/CKt-Core" );

        // A null refName is the bare repository's HEAD: HasDefaultBranch is false, so there is nothing else
        // to mean. This fixture is "master" only (there is no "main" in it).
        var (success, content) = await p.GetFileContentAsync( TestHelper.Monitor, bareCKtStack, "CKt.xml" );
        success.ShouldBeTrue();
        var xml = Encoding.UTF8.GetString( content.ShouldNotBeNull() );
        xml.ShouldContain( "<CKt" );

        // Naming the branch explicitly reads the very same bytes.
        var onMaster = await p.GetFileContentAsync( TestHelper.Monitor, bareCKtStack, "CKt.xml", refName: "master" );
        onMaster.Success.ShouldBeTrue();
        onMaster.Content.ShouldNotBeNull().ShouldBe( content );

        // A commit sha is a refName too.
        using( var repo = new Repository( bareCKtStack.AppendPart( ".git" ) ) )
        {
            var onSha = await p.GetFileContentAsync( TestHelper.Monitor, bareCKtStack, "CKt.xml", refName: repo.Head.Tip.Sha );
            onSha.Success.ShouldBeTrue();
            onSha.Content.ShouldNotBeNull().ShouldBe( content );
        }

        // A file in a folder.
        var nested = await p.GetFileContentAsync( TestHelper.Monitor, bareCKtCore, "CKt.Core/CKt.Core.csproj" );
        nested.Success.ShouldBeTrue();
        Encoding.UTF8.GetString( nested.Content.ShouldNotBeNull() ).ShouldContain( "<Project" );
    }

    [Test]
    public async Task a_missing_file_ref_or_repo_is_not_an_error_Async()
    {
        var p = GetFileHostingProvider();
        TestEnv.OpenRemotes( "CKt" );
        var bareCKtStack = TestHelper.TestProjectFolder.Combine( "Remotes/bare/CKt/CKt-Stack" );

        // The 3 "nothing to read" cases all answer (true,null) and log at the notFoundLogLevel: this is
        // why GetFileContentAsync must not be used to probe for a repository's existence.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var missingFile = await p.GetFileContentAsync( TestHelper.Monitor, bareCKtStack, "No/Way.json",
                                                           notFoundLogLevel: LogLevel.Warn );
            missingFile.Success.ShouldBeTrue();
            missingFile.Content.ShouldBeNull();
            logs.ShouldContain( l => Regex.IsMatch( l, @"File 'No/Way\.json' not found in 'file://.*CKt-Stack'@[0-9a-f]{40}\." ) );

            // "main" is not in this fixture: the CKt-Stack remote is "master" only.
            var missingRef = await p.GetFileContentAsync( TestHelper.Monitor, bareCKtStack, "CKt.xml", refName: "main",
                                                          notFoundLogLevel: LogLevel.Warn );
            missingRef.Success.ShouldBeTrue();
            missingRef.Content.ShouldBeNull();
            logs.ShouldContain( l => Regex.IsMatch( l, @"No branch, tag or commit 'main' in 'file://.*CKt-Stack'\." ) );

            var missingRepo = await p.GetFileContentAsync( TestHelper.Monitor,
                                                           TestHelper.TestProjectFolder.AppendPart( "No way" ),
                                                           "CKt.xml",
                                                           notFoundLogLevel: LogLevel.Warn );
            missingRepo.Success.ShouldBeTrue();
            missingRepo.Content.ShouldBeNull();
            logs.ShouldContain( l => Regex.IsMatch( l, @"Repository 'file://.*No way' not found: no file to read\." ) );
        }
    }

    [Test]
    public async Task reading_a_folder_instead_of_a_file_is_an_error_Async()
    {
        var p = GetFileHostingProvider();
        TestEnv.OpenRemotes( "CKt" );
        var bareCKtCore = TestHelper.TestProjectFolder.Combine( "Remotes/bare/CKt/CKt-Core" );

        // "CKt.Core" exists in the tree but it is a Tree, not a Blob: unlike a missing entry, this is an
        // error - the caller asked for something that is there and is not a file.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var (success, content) = await p.GetFileContentAsync( TestHelper.Monitor, bareCKtCore, "CKt.Core" );
            success.ShouldBeFalse();
            content.ShouldBeNull();
            logs.ShouldContain( l => Regex.IsMatch( l, @"'CKt\.Core' in 'file://.*CKt-Core'@[0-9a-f]{40} is a Tree, not a file\." ) );
        }
    }

    [Test]
    public async Task info_on_existing_bare_git_repo_Async()
    {
        var p = GetFileHostingProvider();

        var bareCKtStack = TestHelper.TestProjectFolder.Combine( "Remotes/bare/CKt/CKt-Stack" );
        var info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, bareCKtStack, mustExist: true );
        info.ShouldNotBeNull();
        info.CloneUrl.ShouldBe( "file://" + bareCKtStack );

        var bareOneStack = TestHelper.TestProjectFolder.Combine( "Remotes/bare/One/One-Stack" );
        info = await p.GetRepositoryInfoAsync( TestHelper.Monitor, bareOneStack, mustExist: true );
        info.ShouldNotBeNull();
        info.CloneUrl.ShouldBe( "file://" + bareOneStack );
    }

    [Test]
    public async Task creating_repo_folder_must_not_exist_Async()
    {
        var p = GetFileHostingProvider();
 
        // The parent of the .git folder must not exist.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var folderExists = TestHelper.TestProjectFolder.Combine( "Remotes/bare/CKt/CKt-Stack" );
            var info = await p.CreateRepositoryAsync( TestHelper.Monitor, folderExists );
            info.ShouldBeNull();
            logs.ShouldContain( l => Regex.IsMatch( l.Replace( '\\', '/' ),
                @"Directory already exists at '.*/CKli/Tests/CKli\.Core\.Tests/Remotes/bare/CKt/CKt-Stack'\." ) );
        }
        // No parent .git folder must exist in the path.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var belowGit = TestHelper.TestProjectFolder.Combine( "Remotes/bare/CKt/CKt-Stack/.git/UnderGit" );
            var info = await p.CreateRepositoryAsync( TestHelper.Monitor, belowGit );
            info.ShouldBeNull();
            logs.ShouldContain( l => Regex.IsMatch( l.Replace( '\\', '/' ),
                @"Cannot create a repository inside another one at '.*/CKli/Tests/CKli\.Core\.Tests/Remotes/bare/CKt/CKt-Stack/\.git/UnderGit'." ) );
        }
        // The repoPath must not end with .git.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var endWithGit = TestHelper.TestProjectFolder.Combine( "Remotes/SomeNew/.git" );
            var info = await p.CreateRepositoryAsync( TestHelper.Monitor, endWithGit );
            info.ShouldBeNull();
            logs.ShouldContain( l => Regex.IsMatch( l.Replace( '\\', '/' ),
                @"Cannot create a repository inside another one at '.*/CKli/Tests/CKli\.Core\.Tests/Remotes/SomeNew/\.git'." ) );
        }
    }

    [Test]
    public async Task creating_repo_Async()
    {
        var p = GetFileHostingProvider();

        // The parent of the .git folder must not exist.
        var tempPath = FileUtil.CreateUniqueTimedFolder( Path.GetTempPath(), "CKli-repo-tests", DateTime.UtcNow );
        try
        {
            var repoPath = new NormalizedPath( tempPath ).AppendPart( "Repo1" );
            var info = await p.CreateRepositoryAsync( TestHelper.Monitor, repoPath, defaultBranchName: "some-default" );
            info.ShouldNotBeNull();
            info.CloneUrl.ShouldNotBeNull().ShouldBe( "file://" + repoPath );

            var clonePath = new NormalizedPath( tempPath ).AppendPart( "Cloned" );
            var uri = new Uri( info.CloneUrl );

            using var cloned = new Repository( Repository.Clone( uri.LocalPath, clonePath ) );
            cloned.ShouldNotBeNull();
            // The default branch name is honored.
            cloned.Head.FriendlyName.ShouldBe( "some-default" );
        }
        finally
        {
            FileHelper.DeleteFolder( TestHelper.Monitor, tempPath );
        }
    }
}
