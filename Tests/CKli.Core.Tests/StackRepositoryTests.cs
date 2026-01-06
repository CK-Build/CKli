using System;
using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class StackRepositoryTests
{
    [Test]
    public void simple_Clone()
    {
        var context = TestEnv.EnsureCleanFolder();
        File.Exists( context.CurrentDirectory.Combine( "CKt/CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeFalse();

        var remotes = TestEnv.OpenRemotes( "CKt" );
        using var stack = StackRepository.Clone( TestHelper.Monitor,
                                                 context,
                                                 remotes.StackUri,
                                                 isPublic: true,
                                                 allowDuplicateStack: false );
        stack.ShouldNotBeNull();
        stack.StackWorkingFolder.LastPart.ShouldBe( ".PublicStack" );
        var localWorldName = stack.GetDefaultWorldName( TestHelper.Monitor );
        localWorldName.ShouldNotBeNull();
        localWorldName.IsDefaultWorld.ShouldBeTrue();
        localWorldName.WorldRoot.ShouldBe( context.CurrentDirectory.AppendPart( "CKt" ) );
        stack.WorldNames.Length.ShouldBe( 1, "There is no LTS world in CKt." );
        stack.WorldNames[0].ShouldBeSameAs( localWorldName );

        File.Exists( context.CurrentDirectory.Combine( "CKt/CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeTrue();
    }

    [Test]
    public void Clone_and_OpenFrom()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );
        using( var stack = StackRepository.Clone( TestHelper.Monitor,
                                                  context,
                                                  remotes.StackUri,
                                                  isPublic: true,
                                                  allowDuplicateStack: false ) )
        {
            stack.ShouldNotBeNull();
        }

        bool error;
        StackRepository.TryOpenFromPath( TestHelper.Monitor, context, out error, skipPullStack: true )
                            .ShouldBeNull( "No stack has been found: the path must be at least in a stack folder." );
        error.ShouldBeFalse( "but this is not an error." );


        var cRoot = context.ChangeDirectory( "CKt" );
        var fromRoot = StackRepository.TryOpenFromPath( TestHelper.Monitor, cRoot, out error, skipPullStack: true )
                                      .ShouldNotBeNull();
        error.ShouldBeFalse();
        fromRoot.Dispose();

        var cSubDir = context.ChangeDirectory( "CKt/CK-Core-Projects" );
        var fromSubDir = StackRepository.TryOpenFromPath( TestHelper.Monitor, cSubDir, out error, skipPullStack: true )
                                        .ShouldNotBeNull();
        error.ShouldBeFalse();
        fromSubDir.Dispose();

        var cRepo = context.ChangeDirectory( "CKt/CK-Core-Projects/CKt-Core" );
        var fromRepo = StackRepository.TryOpenFromPath( TestHelper.Monitor, cRepo, out error, skipPullStack: true )
                                      .ShouldNotBeNull();
        error.ShouldBeFalse();
        fromRepo.Dispose();

        var cInsideRepo = context.ChangeDirectory( "CKt/CK-Core-Projects/CKt-Core/Tests" );
        var fromInsideRepo = StackRepository.TryOpenFromPath( TestHelper.Monitor, cInsideRepo, out error, skipPullStack: true )
                                            .ShouldNotBeNull();
        error.ShouldBeFalse();
        fromInsideRepo.Dispose();
    }

    [Test]
    public void Clone_and_TryOpenWorldFrom()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );

        using( var clone = StackRepository.Clone( TestHelper.Monitor,
                                                  context,
                                                  remotes.StackUri,
                                                  isPublic: true,
                                                  allowDuplicateStack: false ) )
        {
            clone.ShouldNotBeNull();
        }

        bool error;
        var (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, context, out error, skipPullStack: true );
        stack.ShouldBeNull( "No stack has been found: the path must be at least in a stack folder." );
        world.ShouldBeNull( "No world either." );
        error.ShouldBeFalse( "But this is not an error." );

        var inCKt = context.ChangeDirectory( "CKt" );
        (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, inCKt, out error, skipPullStack: true );
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        error.ShouldBeFalse();
        var cktCoreRepo = world.GetDefinedRepo( TestHelper.Monitor, inCKt.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core" ) )
                               .ShouldNotBeNull();
        var cktCoreRepoId = cktCoreRepo.CKliRepoId;
        cktCoreRepoId.IsValid.ShouldBeTrue();
        stack.Dispose();

        var inCKtProject = context.ChangeDirectory( "CKt/CK-Core-Projects" );
        (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, inCKtProject, out error, skipPullStack: true );
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        error.ShouldBeFalse();
        cktCoreRepo = world.GetDefinedRepo( TestHelper.Monitor, inCKtProject.CurrentDirectory.Combine( "CKt-Core" ) )
                           .ShouldNotBeNull();
        cktCoreRepo.CKliRepoId.ShouldBe( cktCoreRepoId );
        stack.Dispose();

        var inCKtProjectCore = context.ChangeDirectory( "CKt/CK-Core-Projects/CKt-Core" );
        (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, inCKtProjectCore, out error, skipPullStack: true );
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        error.ShouldBeFalse();
        cktCoreRepo = world.GetDefinedRepo( TestHelper.Monitor, inCKtProjectCore.CurrentDirectory ).ShouldNotBeNull();
        cktCoreRepo.CKliRepoId.ShouldBe( cktCoreRepoId );
        stack.Dispose();

        var inCKtProjectCoreTests = context.ChangeDirectory( "CKt/CK-Core-Projects/CKt-Core/Tests" );
        (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, inCKtProjectCoreTests, out error, skipPullStack: true );
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        error.ShouldBeFalse();
        // GetDefinedRepo works for sub paths.
        cktCoreRepo = world.GetDefinedRepo( TestHelper.Monitor, inCKtProjectCoreTests.CurrentDirectory )
                           .ShouldNotBeNull();
        cktCoreRepo.CKliRepoId.ShouldBe( cktCoreRepoId );
        stack.Dispose();
    }

    [Test]
    public void Clone_and_OpenWorldFrom()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );

        using( var clone = StackRepository.Clone( TestHelper.Monitor,
                                                  context,
                                                  remotes.StackUri,
                                                  isPublic: true,
                                                  allowDuplicateStack: false ) )
        {
            clone.ShouldNotBeNull();
        }

        StackRepository? stack;
        World? world;

        StackRepository.OpenWorldFromPath( TestHelper.Monitor, context, out stack, out world, skipPullStack: true )
            .ShouldBeFalse( "Here we have an error." );
        stack.ShouldBe( null );
        world.ShouldBeNull();

        var inCKt = context.ChangeDirectory( "CKt" );
        StackRepository.OpenWorldFromPath( TestHelper.Monitor, inCKt, out stack, out world, skipPullStack: true )
            .ShouldBeTrue();
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        stack.Dispose();

        var inCKtProject = context.ChangeDirectory( "CKt/CK-Core-Projects" );
        StackRepository.OpenWorldFromPath( TestHelper.Monitor, inCKtProject, out stack, out world, skipPullStack: true )
            .ShouldBeTrue();
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        stack.Dispose();

        var inCKtProjectCore = context.ChangeDirectory( "CKt/CK-Core-Projects/CKt-Core" );
        StackRepository.OpenWorldFromPath( TestHelper.Monitor, inCKtProjectCore, out stack, out world, skipPullStack: true )
            .ShouldBeTrue();
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        stack.Dispose();

        var inCKtProjectCoreTests = context.ChangeDirectory( "CKt/CK-Core-Projects/CKt-Core/Tests" );
        StackRepository.OpenWorldFromPath( TestHelper.Monitor, inCKtProjectCoreTests, out stack, out world, skipPullStack: true )
            .ShouldBeTrue();
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        stack.Dispose();
    }

    [Test]
    public async Task Add_new_repository_to_Default_World_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );

        // ckli clone file:///.../CKt-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd CKt
        context = context.ChangeDirectory( "CKt" );

        using( TestHelper.Monitor.OpenInfo( "Add the CKt-ActivityMonitor repository at the root and close the stack." ) )
        {
            StackRepository.OpenWorldFromPath( TestHelper.Monitor,
                                               context,
                                               out var stack,
                                               out var world,
                                               skipPullStack: true ).ShouldBeTrue();
            File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "No yet." );
            world.Layout.Count.ShouldBe( 1, "Only CKt-Core in the Layout" );

            world.AddRepository( TestHelper.Monitor,
                                 remotes.GetUriFor( "CKt-ActivityMonitor" ),
                                 stack.DefaultWorldName.WorldRoot ).ShouldBeTrue();

            world.Layout.Count.ShouldBe( 2, "The Layout has been updated." );
            File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Here it is." );

            stack.Dispose();
        }
        using( TestHelper.Monitor.OpenInfo( "Open the stack and check that the definition file has the CKt-ActivityMonitor repository." ) )
        {
            using var readStack = StackRepository.TryOpenFromPath( TestHelper.Monitor, context, out _, skipPullStack: true )
                                                 .ShouldNotBeNull();
            var definitionFile = readStack.DefaultWorldName.LoadDefinitionFile( TestHelper.Monitor ).ShouldNotBeNull();
            definitionFile.XmlRoot.Elements( "Repository" )
                               .ShouldHaveSingleItem()
                               .Attributes( "Url" )
                               .ShouldHaveSingleItem()
                               .Value.ShouldBe( "CKt-ActivityMonitor" );
        }
        using( TestHelper.Monitor.OpenInfo( "Delete the CKt-ActivityMonitor repository working folder and pull the default world's repositories." ) )
        {
            TestEnv.DeleteFolder( context.CurrentDirectory.Combine( "CKt-ActivityMonitor" ) );

            File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "No more." );

            var (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, context, out var error, skipPullStack: true );
            error.ShouldBeFalse();
            stack.ShouldNotBeNull();
            world.ShouldNotBeNull();

            // FixLayout kicks-in here.
            // ckli pull
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "pull" )).ShouldBeTrue();

            File.Exists( context.CurrentDirectory.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Back thanks to the automatic FixLayout." );

            stack.Dispose();
        }
    }

    [Test]
    public async Task DuplicateOf_detection_Async()
    {
        var root = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );

        var initialPath = root.ChangeDirectory( "Initial" );
        var duplicate1 = root.ChangeDirectory( "Duplicate1" );
        var duplicate2 = root.ChangeDirectory( "Duplicate2" );

        // ckli clone file:///.../CKt-Stack -p Initial
        (await CKliCommands.ExecAsync( TestHelper.Monitor, initialPath, "clone", remotes.StackUri )).ShouldBeTrue();

        // ckli clone file:///.../CKt-Stack -p Duplicate1
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, duplicate1, "clone", remotes.StackUri ))
                .ShouldBeFalse( "This fails" );
            logs.Any( log => Regex.Match( log, "The stack 'CKt' at '.*' is already available here" ).Success ).ShouldBeTrue();
        }

        // ckli clone file:///.../CKt-Stack -p Duplicate1 --allow-duplicate
        using( var stack = StackRepository.Clone( TestHelper.Monitor,
                                                  duplicate1,
                                                  remotes.StackUri,
                                                  isPublic: true,
                                                  allowDuplicateStack: true ) )
        {
            stack.ShouldNotBeNull();
            stack.IsDuplicate.ShouldBeTrue();
        }
    }

    [Test]
    public async Task Clone_with_diff_casing_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var display = (StringScreen)context.Screen;
        var remotes = TestEnv.OpenRemotes( "CKt" );

        var stackUrl = remotes.StackUri.LocalPath;
        stackUrl = stackUrl.ToLowerInvariant();
        Assume.That( Directory.Exists( stackUrl ), "This test can only run on case insensitive file system." );

        // ckli clone ...ckt-stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", new Uri( stackUrl ) )).ShouldBeTrue();

        var folders = Directory.EnumerateDirectories( context.CurrentDirectory )
            .Select( path => Path.GetFileName( path ) )
            .ToList();
        folders.ShouldContain( "CKt", "The folder name has been fixed." );
        folders.ShouldNotContain( "ckt", "The folder name has been fixed." );

        // cd CKt
        context = context.ChangeDirectory( "CKt" );

        display.Clear();
        // ckli repo list
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "repo", "list" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ··CK-Core-Projects/CKt-Core·master·↑0↓0·file:///c:/dev/ckli/tests/ckli.core.tests/remotes/bare/ckt/CKt-Core·
            ❰✓❱

            """.Replace( '·', ' ' ) );
    }
}
