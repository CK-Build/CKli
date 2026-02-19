using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests.GitHosting;

[TestFixture]
public class FileSystemProviderTests
{
    [Test]
    public async Task one_instance_for_public_or_private_from_any_file_scheme_Async()
    {
        var secretsStore = new RecordingSecretsStore();

        var gitKey1 = new GitRepositoryKey( secretsStore, new Uri( "C:/Some/path" ), isPublic: true );
        var p1 = gitKey1.AccessKey.HostingProvider;
        p1.ShouldNotBeNull().ProviderType.ShouldBe( "FileSystemProvider" );

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
    public async Task info_on_non_existing_git_or_non_bare_repo_is_an_error_Async()
    {
        var secretsStore = new RecordingSecretsStore();
        var key = new GitRepositoryKey( secretsStore, new Uri( "C:/Some/path" ), isPublic: true );
        var p = key.AccessKey.HostingProvider;
        p.ShouldNotBeNull();

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
    public async Task info_on_existing_bare_git_repo_Async()
    {
        var secretsStore = new RecordingSecretsStore();
        var key = new GitRepositoryKey( secretsStore, new Uri( "C:/Some/path" ), isPublic: true );
        var p = key.AccessKey.HostingProvider;
        p.ShouldNotBeNull();

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
        var secretsStore = new RecordingSecretsStore();
        var key = new GitRepositoryKey( secretsStore, new Uri( "C:/Some/path" ), isPublic: true );
        var p = key.AccessKey.HostingProvider;
        p.ShouldNotBeNull();

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
                @"Cannot create a repository inside another one at '.*/Dev/CKli/Tests/CKli\.Core\.Tests/Remotes/SomeNew/\.git'." ) );
        }
    }

    [Test]
    public async Task creating_repo_without_options_Async()
    {
        var secretsStore = new RecordingSecretsStore();
        var key = new GitRepositoryKey( secretsStore, new Uri( "C:/Some/path" ), isPublic: true );
        var p = key.AccessKey.HostingProvider;
        p.ShouldNotBeNull();

        // The parent of the .git folder must not exist.
        var tempPath = FileUtil.CreateUniqueTimedFolder( Path.GetTempPath(), "CKli-repo-tests", DateTime.UtcNow );
        try
        {
            var repoPath = new NormalizedPath( tempPath ).AppendPart( "Repo1" );
            var info = await p.CreateRepositoryAsync( TestHelper.Monitor, repoPath );
            info.ShouldNotBeNull();
            info.CloneUrl.ShouldNotBeNull().ShouldBe( "file://" + repoPath );

            var clonePath = new NormalizedPath( tempPath ).AppendPart( "Cloned" );
            var uri = new Uri( info.CloneUrl );

            using var cloned = new Repository( Repository.Clone( uri.LocalPath, clonePath ) );
            cloned.ShouldNotBeNull();
            cloned.Head.FriendlyName.ShouldBe( "master" );
        }
        finally
        {
            TestHelper.CleanupFolder( tempPath, ensureFolderAvailable: false );
        }
    }
    [Test]
    public async Task creating_repo_with_options_are_ignored_Async()
    {
        var secretsStore = new RecordingSecretsStore();
        var key = new GitRepositoryKey( secretsStore, new Uri( "C:/Some/path" ), isPublic: true );
        var p = key.AccessKey.HostingProvider;
        p.ShouldNotBeNull();

        // The parent of the .git folder must not exist.
        var tempPath = FileUtil.CreateUniqueTimedFolder( Path.GetTempPath(), "CKli-repo-tests", DateTime.UtcNow );
        try
        {
            var repoPath = new NormalizedPath( tempPath ).AppendPart( "Repo1" );
            var info = await p.CreateRepositoryAsync( TestHelper.Monitor, repoPath );
            info.ShouldNotBeNull();
            info.CloneUrl.ShouldNotBeNull().ShouldBe( "file://" + repoPath );

            var clonePath = new NormalizedPath( tempPath ).AppendPart( "Cloned" );
            var uri = new Uri( info.CloneUrl );

            using var cloned = new Repository( Repository.Clone( uri.LocalPath, clonePath ) );
            cloned.ShouldNotBeNull();
            // Unfortunately...
            cloned.Head.FriendlyName.ShouldBe( "master" );
        }
        finally
        {
            TestHelper.CleanupFolder( tempPath, ensureFolderAvailable: false );
        }
    }
}
