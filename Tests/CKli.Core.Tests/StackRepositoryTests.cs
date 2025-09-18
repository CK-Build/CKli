using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
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

        var remotes = Remotes.UseReadOnly( "CKt" );
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
        var remotes = Remotes.UseReadOnly( "CKt" );
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
        var remotes = Remotes.UseReadOnly( "CKt" );
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
        var remotes = Remotes.UseReadOnly( "CKt" );
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
        var remotes = Remotes.UseReadOnly( "CKt" );

        // ckli clone file:///.../CKt-Stack
        CKliCommands.Clone( TestHelper.Monitor, secretsStore, localPath, remotes.StackUri ).ShouldBe( 0 );
        // cd CKt
        localPath = localPath.AppendPart( "CKt" );

        var addedRepoUri = remotes.GetUriFor( "CKt-ActivityMonitor" );

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
                                 addedRepoUri,
                                 stack.DefaultWorldName.WorldRoot ).ShouldBeTrue();

            world.Layout.Count.ShouldBe( 2, "The Layout has been updated." );
            File.Exists( localPath.Combine( "CKt-ActivityMonitor/CKt-ActivityMonitor.sln" ) ).ShouldBeTrue( "Here it is." );
        }
        using( TestHelper.Monitor.OpenInfo( "Open the stack and check that the definition file has the CKt-ActivityMonitor repository." ) )
        {
            var readStack = StackRepository.TryOpenFromPath( TestHelper.Monitor, secretsStore, localPath, out _, skipPullStack: true )
                                           .ShouldNotBeNull();
            var definitionFile = readStack.DefaultWorldName.LoadDefinitionFile( TestHelper.Monitor ).ShouldNotBeNull();
            definitionFile.Root.Elements( "Repository" )
                               .ShouldHaveSingleItem()
                               .Attributes( "Url" )
                               .ShouldHaveSingleItem()
                               .Value.ShouldBe( addedRepoUri.ToString() );
            readStack.Dispose();
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
}
