using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class StackSetRemoteUrlTests
{
    [Test]
    public void SetRemoteUrl_updates_git_remote_without_push()
    {
        var context = TestEnv.EnsureCleanFolder();

        // Create a stack
        using var stack = StackRepository.Create( TestHelper.Monitor, context,
                                                  "UrlTest", isPublic: true );
        stack.ShouldNotBeNull();

        var oldUrl = stack.OriginUrl;
        var newUrl = new Uri( "file:///tmp/test/NewRemote-Stack" );

        // Change the remote URL without push
        var success = stack.SetRemoteUrl( TestHelper.Monitor, newUrl, push: false );
        success.ShouldBeTrue();

        // Verify the git remote was updated
        using var repo = new Repository( stack.StackWorkingFolder );
        var origin = repo.Network.Remotes["origin"];
        origin.ShouldNotBeNull();
        origin.Url.ShouldBe( newUrl.AbsoluteUri );
    }

    [Test]
    public void SetRemoteUrl_updates_registry()
    {
        var context = TestEnv.EnsureCleanFolder();

        // Create a stack
        using var stack = StackRepository.Create( TestHelper.Monitor, context,
                                                  "RegTest", isPublic: true );
        stack.ShouldNotBeNull();

        var stackPath = stack.StackWorkingFolder;
        var newUrl = new Uri( "file:///tmp/test/RegRemote-Stack" );

        // Change the remote URL
        var success = stack.SetRemoteUrl( TestHelper.Monitor, newUrl, push: false );
        success.ShouldBeTrue();

        // Close the stack properly
        stack.Close( TestHelper.Monitor );

        // Re-read the registry file to verify the URL was updated
        // Use CKliRootEnv.AppLocalDataPath to get the correct path
        var registryPath = CKliRootEnv.AppLocalDataPath.AppendPart( StackRepository.StackRegistryFileName );

        File.Exists( registryPath ).ShouldBeTrue( $"Registry file should exist at {registryPath}" );
        var content = File.ReadAllText( registryPath );
        content.ShouldContain( stackPath.Path );
        content.ShouldContain( newUrl.AbsoluteUri );
    }

    [Test]
    public async Task Command_normalizes_git_suffix_Async()
    {
        var context = TestEnv.EnsureCleanFolder();

        // Create stack first
        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "create", "NormTest" ) ).ShouldBeTrue();

        context = context.ChangeDirectory( "NormTest" );

        // URL with .git suffix should be normalized by the command
        var urlWithGit = "file:///tmp/test/NormRemote-Stack.git";
        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "set-remote-url", urlWithGit, "--no-push" ) ).ShouldBeTrue();

        // Verify .git suffix was stripped
        var stackPath = context.CurrentDirectory.AppendPart( ".PublicStack" );
        using var repo = new Repository( stackPath );
        var origin = repo.Network.Remotes["origin"];
        origin.ShouldNotBeNull();
        origin.Url.ShouldNotEndWith( ".git" );
    }

    [Test]
    public void SetRemoteUrl_detects_equivalent_url()
    {
        var context = TestEnv.EnsureCleanFolder();

        using var stack = StackRepository.Create( TestHelper.Monitor, context,
                                                  "EquivTest", isPublic: true );
        stack.ShouldNotBeNull();

        var currentUrl = stack.OriginUrl;

        // Setting the same URL should succeed without changes
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var success = stack.SetRemoteUrl( TestHelper.Monitor, currentUrl, push: false );
            success.ShouldBeTrue();
            logs.Any( l => l.Contains( "already" ) ).ShouldBeTrue();
        }
    }

    [Test]
    public async Task stack_set_remote_url_command_Async()
    {
        var context = TestEnv.EnsureCleanFolder();

        // Create stack first
        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "create", "CmdUrlTest" ) ).ShouldBeTrue();

        context = context.ChangeDirectory( "CmdUrlTest" );

        // Change the remote URL via command
        var newUrl = "file:///tmp/test/CmdRemote-Stack";
        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "set-remote-url", newUrl, "--no-push" ) ).ShouldBeTrue();

        // Verify
        var stackPath = context.CurrentDirectory.AppendPart( ".PublicStack" );
        using var repo = new Repository( stackPath );
        var origin = repo.Network.Remotes["origin"];
        origin.ShouldNotBeNull();
        origin.Url.ShouldBe( newUrl );
    }

    [Test]
    public async Task stack_set_remote_url_fails_outside_stack_Async()
    {
        var context = TestEnv.EnsureCleanFolder();

        // Try to set URL without being in a stack
        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "set-remote-url", "file:///tmp/test/NoStack-Stack", "--no-push" ) ).ShouldBeFalse();
    }

    [Test]
    public async Task stack_set_remote_url_with_invalid_url_fails_Async()
    {
        var context = TestEnv.EnsureCleanFolder();

        // Create stack first
        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "create", "InvalidUrlTest" ) ).ShouldBeTrue();

        context = context.ChangeDirectory( "InvalidUrlTest" );

        // Relative URL should fail validation
        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "set-remote-url", "not-a-valid-url", "--no-push" ) ).ShouldBeFalse();
    }

    [Test]
    public void SetRemoteUrl_rejects_invalid_url()
    {
        var context = TestEnv.EnsureCleanFolder();

        using var stack = StackRepository.Create( TestHelper.Monitor, context,
                                                  "InvalidUrlTest", isPublic: true );
        stack.ShouldNotBeNull();

        // Relative URL should be rejected
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            // The CheckAndNormalizeRepositoryUrl validates this at the command level,
            // but the method itself requires absolute URI
            var relativeUrl = new Uri( "relative/path", UriKind.Relative );
            Should.Throw<ArgumentException>( () => stack.SetRemoteUrl( TestHelper.Monitor, relativeUrl, push: false ) );
        }
    }
}
