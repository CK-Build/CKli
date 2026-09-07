using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// "ckli remote stack migrate" moves the Stack repository to a new remote: it creates the new repository,
/// changes the "origin" url, pushes the content and archives the previous one. The file system provider
/// cannot archive: these tests check that this is a warning, not a failure.
/// </summary>
[TestFixture]
public class RemoteStackMigrateTests
{
    // Migrating pushes: the write PAT for the "FILESYSTEM" is required.
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    [Test]
    public async Task migrate_creates_the_remote_pushes_the_content_and_is_idempotent_Async()
    {
        var root = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );

        // ckli clone file:///.../CKt-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, root, "clone", remotes.StackUri )).ShouldBeTrue();
        var inStack = root.ChangeDirectory( "CKt" );
        // This file must reach the new remote.
        File.WriteAllText( inStack.CurrentDirectory.Combine( ".PublicStack/Some.txt" ), "Migrated!" );

        var newUrl = new Uri( root.CurrentDirectory.Combine( "NewRemote/CKt-Stack" ) );
        Directory.Exists( newUrl.LocalPath ).ShouldBeFalse( "The new repository doesn't exist yet." );

        // ckli remote stack migrate file:///.../NewRemote/CKt-Stack
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "remote", "stack", "migrate", newUrl )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "cannot archive a repository" ),
                                "The FileSystemProvider cannot archive: this is a warning." );
        }
        Directory.Exists( newUrl.LocalPath ).ShouldBeTrue( "The new repository has been created." );
        using( var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, inStack, out _, skipPullStack: true )
                                          .ShouldNotBeNull() )
        {
            stack.GitRepository.RepositoryKey.OriginUrl.ShouldBe( newUrl );
            stack.MigrationSourceUrl.ShouldBeNull( "The previous repository reached its final state." );
        }
        CheckNewRemoteContent( root, newUrl, "Check1", "Migrated!" );

        // Idempotent: the url has already moved and nothing is pending.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "remote", "stack", "migrate", newUrl )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "already" ) );
            logs.ShouldNotContain( l => l.Contains( "cannot archive a repository" ), "Nothing to archive anymore." );
        }

        // A migration interrupted before the previous repository reached its final state leaves the
        // MigrationSourceUrl behind: another run finishes the job.
        using( var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, inStack, out _, skipPullStack: true )
                                          .ShouldNotBeNull() )
        {
            stack.SetMigrationSourceUrl( TestHelper.Monitor, remotes.StackUri ).ShouldBeTrue();
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "remote", "stack", "migrate", newUrl )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "cannot archive a repository" ), "The pending previous repository is handled." );
        }
        using( var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, inStack, out _, skipPullStack: true )
                                          .ShouldNotBeNull() )
        {
            stack.MigrationSourceUrl.ShouldBeNull();
        }
    }

    [Test]
    public async Task migrate_cannot_rename_the_Stack_Async()
    {
        var root = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, root, "clone", remotes.StackUri )).ShouldBeTrue();
        var inStack = root.ChangeDirectory( "CKt" );

        var otherUrl = new Uri( root.CurrentDirectory.Combine( "NewRemote/Other-Stack" ) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "remote", "stack", "migrate", otherUrl )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "a migration cannot rename a Stack" ) );
        }
        Directory.Exists( otherUrl.LocalPath ).ShouldBeFalse( "Nothing has been created." );
        using var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, inStack, out _, skipPullStack: true )
                                         .ShouldNotBeNull();
        stack.GitRepository.RepositoryKey.OriginUrl.ShouldBe( remotes.StackUri, "The origin is untouched." );
        stack.MigrationSourceUrl.ShouldBeNull();
    }

    [Test]
    public async Task SetRemoteUrl_pushes_at_the_end_Async()
    {
        var root = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, root, "clone", remotes.StackUri )).ShouldBeTrue();
        var inStack = root.ChangeDirectory( "CKt" );

        // A bare clone of the current remote: it shares the Stack history, so no forced push is needed.
        var cloneUrl = new Uri( Repository.Clone( remotes.StackUri.AbsoluteUri,
                                                  root.CurrentDirectory.Combine( "Bare/CKt-Stack" ),
                                                  new CloneOptions { IsBare = true } ) );
        File.WriteAllText( inStack.CurrentDirectory.Combine( ".PublicStack/Some.txt" ), "Pushed by SetRemoteUrl!" );
        using( var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, inStack, out _, skipPullStack: true )
                                          .ShouldNotBeNull() )
        {
            var bareUrl = new Uri( root.CurrentDirectory.Combine( "Bare/CKt-Stack" ) );
            stack.SetRemoteUrl( TestHelper.Monitor, bareUrl, push: true ).ShouldBeTrue();
            stack.MigrationSourceUrl.ShouldBe( remotes.StackUri, "SetRemoteUrl remembers where it comes from." );
        }
        CheckNewRemoteContent( root, new Uri( root.CurrentDirectory.Combine( "Bare/CKt-Stack" ) ), "Check", "Pushed by SetRemoteUrl!" );
    }

    static void CheckNewRemoteContent( CKliEnv root, Uri remoteUrl, string folderName, string content )
    {
        var path = root.CurrentDirectory.AppendPart( folderName );
        using var check = GitRepository.Clone( TestHelper.Monitor,
                                               new GitRepositoryKey( root.SecretsStore, remoteUrl, true ),
                                               root.Committer,
                                               path,
                                               path.LastPart ).ShouldNotBeNull();
        File.Exists( check.WorkingFolder.AppendPart( "CKt.xml" ) ).ShouldBeTrue( "The Stack definition is there." );
        File.ReadAllText( check.WorkingFolder.AppendPart( "Some.txt" ) ).ShouldBe( content );
    }
}
