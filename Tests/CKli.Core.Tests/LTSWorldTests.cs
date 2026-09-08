using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// A Stack has one default World and any number of Long Term Support Worlds, each one defined by a
/// "StackName@ltsName.xml" file in the Stack repository and rooted in its own "@ltsName/" folder.
/// <para>
/// "ckli clone --lts-name" clones a Stack at one of its LTS worlds and "ckli lts clone" obtains one in
/// an already cloned Stack.
/// </para>
/// </summary>
[TestFixture]
public class LTSWorldTests
{
    // The arranges push the LTS world definition file to the Stack remote.
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    [Test]
    public async Task clone_lts_name_clones_the_LTS_world_repositories_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );
        ArrangeLTSWorld( context, one.StackUri, "One", "@net8" );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", one.StackUri, "--lts-name", "@net8" ))
            .ShouldBeTrue();

        Directory.Exists( target.CurrentDirectory.Combine( "One/@net8/OneRepo" ) )
                 .ShouldBeTrue( "The LTS world's repositories are cloned in its own folder." );
        Directory.Exists( target.CurrentDirectory.Combine( "One/OneRepo" ) )
                 .ShouldBeFalse( "The default world is not the one that has been cloned." );
    }

    [Test]
    public async Task clone_lts_name_must_be_a_valid_LTS_name_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", one.StackUri, "--lts-name", "Net8" ))
                .ShouldBeFalse( "A LTS name starts with '@' and is lower case." );
            logs.ShouldContain( l => l.Contains( "Invalid --lts-name 'Net8'." ) );
        }
        Directory.Exists( context.CurrentDirectory.AppendPart( "One" ) ).ShouldBeFalse( "Nothing has been cloned." );
    }

    [Test]
    public async Task lts_clone_obtains_a_world_in_an_already_cloned_Stack_and_is_idempotent_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );
        ArrangeLTSWorld( context, one.StackUri, "One", "@net8" );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        // The default world is cloned: the "@net8" world exists in the Stack repository but has no folder.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", one.StackUri )).ShouldBeTrue();
        var stackRoot = target.CurrentDirectory.AppendPart( "One" );
        Directory.Exists( stackRoot.AppendPart( "@net8" ) ).ShouldBeFalse();

        var inStack = target.ChangeDirectory( stackRoot );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "lts", "clone", "@net8" )).ShouldBeTrue();
        Directory.Exists( stackRoot.Combine( "@net8/OneRepo" ) ).ShouldBeTrue();
        Directory.Exists( stackRoot.AppendPart( "OneRepo" ) ).ShouldBeTrue( "The default world is untouched." );

        // Opening a world creates its plugin solution in the Stack repository: it must have been committed.
        using( var git = new LibGit2Sharp.Repository( stackRoot.AppendPart( ".PublicStack" ) ) )
        {
            git.RetrieveStatus( new LibGit2Sharp.StatusOptions() ).IsDirty
               .ShouldBeFalse( "The Stack repository has been committed." );
            git.Index.Select( e => e.Path )
               .ShouldContain( $"@net8/One-Plugins@net8/One-Plugins@net8.slnx",
                               customMessage: "The LTS world's plugin solution is tracked." );
        }

        // Idempotent: nothing left to do.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "lts", "clone", "@net8" )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "has no repository to clone" ) );
        }
    }

    [Test]
    public async Task lts_clone_requires_an_existing_world_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", one.StackUri )).ShouldBeTrue();
        var inStack = context.ChangeDirectory( "One" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inStack, "lts", "clone", "@net8" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "Stack 'One' has no '@net8' Long Term Support world." ) );
        }
    }

    /// <summary>
    /// The default world's layout must ignore the LTS worlds' folders. Without this, its repositories look
    /// misplaced: "layout fix" moves them out of the LTS world (and "layout xif" adopts them).
    /// </summary>
    [Test]
    public async Task the_default_world_layout_ignores_the_LTS_world_folders_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var one = TestEnv.OpenRemotes( "One" );
        ArrangeLTSWorld( context, one.StackUri, "One", "@net8" );

        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var target = context.ChangeDirectory( "Target" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, target, "clone", one.StackUri, "--lts-name", "@net8" ))
            .ShouldBeTrue();
        var stackRoot = target.CurrentDirectory.AppendPart( "One" );
        var inDefaultWorld = target.ChangeDirectory( stackRoot );

        // The default world has no repository cloned and the "@net8" world has OneRepo.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inDefaultWorld, "layout", "fix" )).ShouldBeTrue();

        Directory.Exists( stackRoot.Combine( "@net8/OneRepo" ) )
                 .ShouldBeTrue( "The LTS world's repository has not been moved out of it." );
        Directory.Exists( stackRoot.AppendPart( "OneRepo" ) )
                 .ShouldBeTrue( "The default world cloned its own copy." );
    }

    /// <summary>
    /// Pushes a "{name}@{ltsName}.xml" world definition file to the Stack remote. Its layout is the same
    /// single repository as the fixture's default world.
    /// </summary>
    static void ArrangeLTSWorld( CKliEnv context, Uri stackUri, string name, string ltsName )
    {
        var path = context.CurrentDirectory.Combine( "Arrange" ).AppendPart( name );
        using var git = GitRepository.Clone( TestHelper.Monitor,
                                             new GitRepositoryKey( context.SecretsStore, stackUri, isPublic: true ),
                                             context.Committer,
                                             path,
                                             path.LastPart ).ShouldNotBeNull();
        // An LTS world definition file must carry its LTSName on its root element.
        File.WriteAllText( git.WorkingFolder.AppendPart( $"{name}@{ltsName[1..]}.xml" ),
                           $"""
                            <{name} LTSName="{ltsName}">
                              <Repository Url="OneRepo" />
                            </{name}>
                            """ );
        git.Commit( TestHelper.Monitor, $"Added '{ltsName}' world." ).ShouldBe( CommitResult.Committed );
        git.PushBranch( TestHelper.Monitor, git.Repository.Head, autoCreateRemoteBranch: true ).ShouldBeTrue();
    }
}
