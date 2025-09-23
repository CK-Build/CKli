using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class StackRepositoryTests
{
    [Test]
    public void simple_Clone()
    {
        var localPath = ClonedPaths.EnsureCleanFolder();
        File.Exists( localPath.Combine( "CKt/CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeFalse();

        var remotes = TestEnv.UseReadOnly( "CKt" );
        using var stack = StackRepository.Clone( TestHelper.Monitor,
                                                 new DotNetUserSecretsStore(),
                                                 remotes.StackUri,
                                                 isPublic: true,
                                                 localPath,
                                                 allowDuplicateStack: false );
        stack.ShouldNotBeNull();
        stack.StackWorkingFolder.LastPart.ShouldBe( ".PublicStack" );
        var localWorldName = stack.GetDefaultWorldName( TestHelper.Monitor );
        localWorldName.ShouldNotBeNull();
        localWorldName.IsDefaultWorld.ShouldBeTrue();
        localWorldName.WorldRoot.ShouldBe( localPath.AppendPart( "CKt" ) );
        stack.WorldNames.Length.ShouldBe( 1, "There is no LTS world in CKt." );
        stack.WorldNames[0].ShouldBeSameAs( localWorldName );

        File.Exists( localPath.Combine( "CKt/CK-Core-Projects/CKt-Core/CKt-Core.sln" ) ).ShouldBeTrue();
    }

    [Test]
    public void Clone_and_OpenFrom()
    {
        var localPath = ClonedPaths.EnsureCleanFolder();
        var remotes = TestEnv.UseReadOnly( "CKt" );
        var secretsStore = new DotNetUserSecretsStore();
        using( var stack = StackRepository.Clone( TestHelper.Monitor,
                                                  secretsStore,
                                                  remotes.StackUri,
                                                  isPublic: true,
                                                  localPath,
                                                  allowDuplicateStack: false ) )
        {
            stack.ShouldNotBeNull();
        }

        bool error;
        StackRepository.TryOpenFromPath( TestHelper.Monitor, secretsStore, localPath, out error, skipPullStack: true )
                            .ShouldBeNull( "No stack has been found: the path must be at least in a stack folder." );
        error.ShouldBeFalse( "but this is not an error." );

        var fromRoot = StackRepository.TryOpenFromPath( TestHelper.Monitor, secretsStore, localPath.AppendPart( "CKt" ), out error, skipPullStack: true )
                                      .ShouldNotBeNull();
        error.ShouldBeFalse();
        fromRoot.Dispose();

        var fromSubDir = StackRepository.TryOpenFromPath( TestHelper.Monitor, secretsStore, localPath.Combine( "CKt/CK-Core-Projects" ), out error, skipPullStack: true )
                                        .ShouldNotBeNull();
        error.ShouldBeFalse();
        fromSubDir.Dispose();

        var fromRepo = StackRepository.TryOpenFromPath( TestHelper.Monitor, secretsStore, localPath.Combine( "CKt/CK-Core-Projects/CKt-Core" ), out error, skipPullStack: true )
                                      .ShouldNotBeNull();
        error.ShouldBeFalse();
        fromRepo.Dispose();

        var fromInsideRepo = StackRepository.TryOpenFromPath( TestHelper.Monitor, secretsStore, localPath.Combine( "CKt/CK-Core-Projects/CKt-Core/Tests" ), out error, skipPullStack: true )
                                            .ShouldNotBeNull();
        error.ShouldBeFalse();
        fromInsideRepo.Dispose();
    }

    [Test]
    public void Clone_and_TryOpenWorldFrom()
    {
        var localPath = ClonedPaths.EnsureCleanFolder();
        var remotes = TestEnv.UseReadOnly( "CKt" );
        var secretsStore = new DotNetUserSecretsStore();
        using( var clone = StackRepository.Clone( TestHelper.Monitor,
                                                  secretsStore,
                                                  remotes.StackUri,
                                                  isPublic: true,
                                                  localPath,
                                                  allowDuplicateStack: false ) )
        {
            clone.ShouldNotBeNull();
        }

        bool error;
        var (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath, out error, skipPullStack: true );
        stack.ShouldBeNull( "No stack has been found: the path must be at least in a stack folder." );
        world.ShouldBeNull( "No world either." );
        error.ShouldBeFalse( "But this is not an error." );

        (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath.AppendPart( "CKt" ), out error, skipPullStack: true );
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        error.ShouldBeFalse();
        stack.Dispose();

        (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath.Combine( "CKt/CK-Core-Projects" ), out error, skipPullStack: true );
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        error.ShouldBeFalse();
        stack.Dispose();

        (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath.Combine( "CKt/CK-Core-Projects/CKt-Core" ), out error, skipPullStack: true );
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        error.ShouldBeFalse();
        stack.Dispose();

        (stack, world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath.Combine( "CKt/CK-Core-Projects/CKt-Core/Tests" ), out error, skipPullStack: true );
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        error.ShouldBeFalse();
        stack.Dispose();
    }

    [Test]
    public void Clone_and_OpenWorldFrom()
    {
        var localPath = ClonedPaths.EnsureCleanFolder();
        var remotes = TestEnv.UseReadOnly( "CKt" );
        var secretsStore = new DotNetUserSecretsStore();
        using( var clone = StackRepository.Clone( TestHelper.Monitor,
                                                  secretsStore,
                                                  remotes.StackUri,
                                                  isPublic: true,
                                                  localPath,
                                                  allowDuplicateStack: false ) )
        {
            clone.ShouldNotBeNull();
        }

        StackRepository? stack;
        World? world;

        StackRepository.OpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath, out stack, out world, skipPullStack: true )
            .ShouldBeFalse( "Here we have an error." );
        stack.ShouldBe( null );
        world.ShouldBeNull();

        StackRepository.OpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath.AppendPart( "CKt" ), out stack, out world, skipPullStack: true )
            .ShouldBeTrue();
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        stack.Dispose();

        StackRepository.OpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath.Combine( "CKt/CK-Core-Projects" ), out stack, out world, skipPullStack: true )
            .ShouldBeTrue();
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        stack.Dispose();

        StackRepository.OpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath.Combine( "CKt/CK-Core-Projects/CKt-Core" ), out stack, out world, skipPullStack: true )
            .ShouldBeTrue();
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        stack.Dispose();

        StackRepository.OpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath.Combine( "CKt/CK-Core-Projects/CKt-Core/Tests" ), out stack, out world, skipPullStack: true )
            .ShouldBeTrue();
        stack.ShouldNotBeNull();
        world.ShouldNotBeNull();
        stack.Dispose();
    }

    [Test]
    public void Add_new_repository_to_Default_World()
    {
        var localPath = ClonedPaths.EnsureCleanFolder();
        var secretsStore = new DotNetUserSecretsStore();
        var remotes = TestEnv.UseReadOnly( "CKt" );

        // ckli clone file:///.../CKt-Stack
        CKliCommands.Clone( TestHelper.Monitor, secretsStore, localPath, remotes.StackUri ).ShouldBe( 0 );
        // cd CKt
        localPath = localPath.AppendPart( "CKt" );

        using( TestHelper.Monitor.OpenInfo( "Add the CKt-ActivityMonitor repository at the root and close the stack." ) )
        {
            StackRepository.OpenWorldFromPath( TestHelper.Monitor,
                                               secretsStore,
                                               localPath,
                                               out var stack,
                                               out var world,
                                               skipPullStack: true ).ShouldBeTrue();
            File.Exists( localPath.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "No yet." );
            world.Layout.Count.ShouldBe( 1, "Only CKt-Core in the Layout" );

            world.AddRepository( TestHelper.Monitor,
                                 remotes.GetUriFor( "CKt-ActivityMonitor" ),
                                 stack.DefaultWorldName.WorldRoot ).ShouldBeTrue();

            world.Layout.Count.ShouldBe( 2, "The Layout has been updated." );
            File.Exists( localPath.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Here it is." );

            stack.Dispose();
        }
        using( TestHelper.Monitor.OpenInfo( "Open the stack and check that the definition file has the CKt-ActivityMonitor repository." ) )
        {
            using var readStack = StackRepository.TryOpenFromPath( TestHelper.Monitor, secretsStore, localPath, out _, skipPullStack: true )
                                                 .ShouldNotBeNull();
            var definitionFile = readStack.DefaultWorldName.LoadDefinitionFile( TestHelper.Monitor ).ShouldNotBeNull();
            definitionFile.Root.Elements( "Repository" )
                               .ShouldHaveSingleItem()
                               .Attributes( "Url" )
                               .ShouldHaveSingleItem()
                               .Value.ShouldBe( "CKt-ActivityMonitor" );
        }
        using( TestHelper.Monitor.OpenInfo( "Delete the CKt-ActivityMonitor repository working folder and pull the default world's repositories." ) )
        {
            ClonedPaths.DeleteClonedFolderOnly( localPath.Combine( "CKt-ActivityMonitor" ) ).ShouldBeTrue( "This is a git working folder." );

            File.Exists( localPath.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeFalse( "No more." );

            var (stack,world) = StackRepository.TryOpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath, out var error, skipPullStack: true );
            error.ShouldBeFalse();
            stack.ShouldNotBeNull();
            world.ShouldNotBeNull();

            // FixLayout kicks-in here.
            world.Pull( TestHelper.Monitor ).ShouldBe( true );

            File.Exists( localPath.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Back thanks to the automatic FixLayout." );

            stack.Dispose();
        }
    }

    [Test]
    public void DuplicateOf_detection()
    {
        var root = ClonedPaths.EnsureCleanFolder();
        var secretsStore = new DotNetUserSecretsStore();
        var remotes = TestEnv.UseReadOnly( "CKt" );

        var initialPath = root.AppendPart( "Initial" );
        var duplicate1 = root.AppendPart( "Duplicate1" );
        var duplicate2 = root.AppendPart( "Duplicate2" );

        // ckli clone file:///.../CKt-Stack -p Initial
        CKliCommands.Clone( TestHelper.Monitor, secretsStore, initialPath, remotes.StackUri ).ShouldBe( 0 );

        // ckli clone file:///.../CKt-Stack -p Duplicate1
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            CKliCommands.Clone( TestHelper.Monitor, secretsStore, duplicate1, remotes.StackUri )
                .ShouldNotBe( 0, "This fails" );
            logs.Any( log => Regex.Match( log, "The stack 'CKt' at '.*' is already available here" ).Success ).ShouldBeTrue();
        }

        // ckli clone file:///.../CKt-Stack -p Duplicate1 --allow-duplicate
        using( var stack = StackRepository.Clone( TestHelper.Monitor, secretsStore, remotes.StackUri, isPublic: true, duplicate1, allowDuplicateStack: true ) )
        {
            stack.ShouldNotBeNull();
            stack.IsDuplicate.ShouldBeTrue();
        }
    }
}
