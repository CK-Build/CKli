using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CK.Core;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class StackCreateTests
{
    [Test]
    public void Create_local_stack_without_remote()
    {
        var context = TestEnv.EnsureCleanFolder();

        using var stack = StackRepository.Create( TestHelper.Monitor, context,
                                                  "TestStack", isPublic: true );
        stack.ShouldNotBeNull();
        stack.StackName.ShouldBe( "TestStack" );
        stack.IsPublic.ShouldBeTrue();
        stack.StackWorkingFolder.LastPart.ShouldBe( ".PublicStack" );

        // Verify definition file exists
        var defFile = stack.StackWorkingFolder.AppendPart( "TestStack.xml" );
        File.Exists( defFile ).ShouldBeTrue();

        // Verify git repository initialized
        Directory.Exists( stack.StackWorkingFolder.AppendPart( ".git" ) ).ShouldBeTrue();

        // Verify $Local and .gitignore created
        Directory.Exists( stack.StackWorkingFolder.AppendPart( "$Local" ) ).ShouldBeTrue();
        File.Exists( stack.StackWorkingFolder.AppendPart( ".gitignore" ) ).ShouldBeTrue();
    }

    [Test]
    public void Create_private_stack()
    {
        var context = TestEnv.EnsureCleanFolder();

        using var stack = StackRepository.Create( TestHelper.Monitor, context,
                                                  "PrivateTest", isPublic: false );
        stack.ShouldNotBeNull();
        stack.IsPublic.ShouldBeFalse();
        stack.StackWorkingFolder.LastPart.ShouldBe( ".PrivateStack" );
    }

    [Test]
    public void Create_and_reopen_stack()
    {
        var context = TestEnv.EnsureCleanFolder();

        // Create the stack
        NormalizedPath stackWorkingFolder;
        using( var stack = StackRepository.Create( TestHelper.Monitor, context,
                                                    "ReopenTest", isPublic: true ) )
        {
            stack.ShouldNotBeNull();
            stackWorkingFolder = stack.StackWorkingFolder;
        }

        // Verify .PublicStack directory still exists after dispose
        Directory.Exists( stackWorkingFolder ).ShouldBeTrue( $".PublicStack should exist at {stackWorkingFolder}" );

        // Reopen the stack
        var stackContext = context.ChangeDirectory( "ReopenTest" );
        stackContext.CurrentStackPath.IsEmptyPath.ShouldBeFalse(
            $"CurrentStackPath should not be empty after ChangeDirectory. CurrentDirectory={stackContext.CurrentDirectory}" );

        using var reopened = StackRepository.TryOpenFromPath( TestHelper.Monitor, stackContext,
                                                               out bool error, skipPullStack: true );
        error.ShouldBeFalse();
        reopened.ShouldNotBeNull();
        reopened.StackName.ShouldBe( "ReopenTest" );
    }

    [Test]
    public void Create_fails_if_path_exists()
    {
        var context = TestEnv.EnsureCleanFolder();

        // Create first stack
        using( var stack1 = StackRepository.Create( TestHelper.Monitor, context,
                                                     "ExistingStack", isPublic: true ) )
        {
            stack1.ShouldNotBeNull();
        }

        // Try to create again - should fail
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var stack2 = StackRepository.Create( TestHelper.Monitor, context,
                                                  "ExistingStack", isPublic: true );
            stack2.ShouldBeNull();
            logs.Any( l => l.Contains( "already exists" ) ).ShouldBeTrue();
        }
    }

    [Test]
    public void Create_fails_inside_existing_stack()
    {
        var context = TestEnv.EnsureCleanFolder();

        // Create outer stack
        using( var outer = StackRepository.Create( TestHelper.Monitor, context,
                                                    "OuterStack", isPublic: true ) )
        {
            outer.ShouldNotBeNull();
        }

        // Try to create nested stack
        var nestedContext = context.ChangeDirectory( "OuterStack" );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var nested = StackRepository.Create( TestHelper.Monitor, nestedContext,
                                                  "InnerStack", isPublic: true );
            nested.ShouldBeNull();
            logs.Any( l => l.Contains( "inside" ) || l.Contains( "existing stack" ) ).ShouldBeTrue();
        }
    }

    [Test]
    public async Task stack_create_command_Async()
    {
        var context = TestEnv.EnsureCleanFolder();

        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "create", "CmdTest" ) ).ShouldBeTrue();

        // Verify stack was created
        Directory.Exists( context.CurrentDirectory.Combine( "CmdTest/.PublicStack" ) )
            .ShouldBeTrue();
    }

    [Test]
    public async Task stack_create_with_private_flag_Async()
    {
        var context = TestEnv.EnsureCleanFolder();

        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "create", "PrivCmd", "--private" ) ).ShouldBeTrue();

        Directory.Exists( context.CurrentDirectory.Combine( "PrivCmd/.PrivateStack" ) )
            .ShouldBeTrue();
    }

    [Test]
    public async Task stack_info_displays_stack_details_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var display = (StringScreen)context.Screen;

        // Create stack first
        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "create", "InfoTest" ) ).ShouldBeTrue();

        context = context.ChangeDirectory( "InfoTest" );
        display.Clear();

        // Run stack info
        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "info" ) ).ShouldBeTrue();

        var output = display.ToString();
        output.ShouldContain( "InfoTest" );
        output.ShouldContain( "main" );  // Branch name
    }

    [Test]
    public async Task stack_info_fails_outside_stack_Async()
    {
        var context = TestEnv.EnsureCleanFolder();

        // Run stack info without being in a stack
        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context,
                                         "stack", "info" ) ).ShouldBeFalse();
    }
}
