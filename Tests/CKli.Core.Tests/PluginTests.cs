using CK.Core;
using CK.Monitoring.InterProcess;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class PluginTests
{
    [Test]
    public async Task create_plugin_request_Info_and_remove_it_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.UseReadOnly( "One" );

        // ckli clone file:///.../One-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd One
        context = context.ChangeDirectory( "One" );

        // ckli plugin create MyFirstOne
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "create", "MyFirstOne" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            // ckli plugin
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin" )).ShouldBeTrue();

            logs.ShouldContain( "New 'MyFirstOne' in world 'One' plugin certainly requires some development." );

            var screen = context.Screen.ToString();
            screen.ShouldContain( """
                1 loaded plugins, 1 configured plugins.

                > MyFirstOne          > <MyFirstOne />
                │    Available        │ 
                │    <source based>   │ 
                │ Message:
                │    Message from 'MyFirstOne' plugin.
                
                """ );
        }

        // ckli plugin remove MyFirstOne
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "remove", "MyFirstOne" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            // ckli plugin
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin" )).ShouldBeTrue();
            logs.ShouldNotContain( "New 'MyFirstOne' in world 'One' plugin certainly requires some development." );
        }
    }

    [Test]
    public async Task CommandSample_package_echo_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.UseReadOnly( "One" );

        TestEnv.EnsurePluginPackage( "CKli.CommandSample.Plugin" );

        // ckli clone file:///.../One-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd One
        context = context.ChangeDirectory( "One" );

        // ckli plugin add CommandSample@version
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "add", $"CommandSample@{TestEnv.CKliPluginsCoreVersion}" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "echo", "hello n°1" )).ShouldBeTrue();
            logs.ShouldContain( "echo: hello n°1" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "echo", "hello n°2", "--upper-case" )).ShouldBeTrue();
            logs.ShouldContain( "echo: HELLO N°2" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "get-world-name" )).ShouldBeTrue();
            logs.ShouldContain( "get-world-name: One" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "get-world-name", "-l" )).ShouldBeTrue();
            logs.ShouldContain( "get-world-name: one" );

        }

        // ckli plugin remove CommandSample
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "remove", "CommandSample" )).ShouldBeTrue();

    }

    [Test]
    public async Task CommandSample_package_config_edit_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.UseReadOnly( "One" );

        TestEnv.EnsurePluginPackage( "CKli.CommandSample.Plugin" );

        // ckli clone file:///.../One-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd One
        context = context.ChangeDirectory( "One" );

        // ckli plugin add CommandSample@version
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "add", $"CommandSample@{TestEnv.CKliPluginsCoreVersion}" )).ShouldBeTrue();

        var definitionFile = XDocument.Load( context.CurrentStackPath.AppendPart( "One.xml" ) );
        var config = definitionFile.Element( "One" )?.Element( "Plugins" )?.Element( "CommandSample" );
        config.ShouldNotBeNull().Value.ShouldBe( "Initial Description..." );


        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "config", "edit", "New Description!" )).ShouldBeTrue();

            definitionFile = XDocument.Load( context.CurrentStackPath.AppendPart( "One.xml" ) );
            config = definitionFile.Element( "One" )?.Element( "Plugins" )?.Element( "CommandSample" );
            config.ShouldNotBeNull().Value.ShouldBe( "New Description!" );
        }

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "config", "edit", "Will fail!", "--remove-plugin-configuration" ))
                .ShouldBeFalse();

            logs.ShouldContain( """
                Plugin 'CommandSample' error while editing configuration:
                <CommandSample>Will fail!</CommandSample>
                """ );

            definitionFile = XDocument.Load( context.CurrentStackPath.AppendPart( "One.xml" ) );
            config = definitionFile.Element( "One" )?.Element( "Plugins" )?.Element( "CommandSample" );
            config.ShouldNotBeNull().Value.ShouldBe( "New Description!", "Definition file is not saved on error." );
        }

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "config", "edit", "This will work but does nothing.", "--rename-plugin-configuration" ))
                .ShouldBeTrue();

            definitionFile = XDocument.Load( context.CurrentStackPath.AppendPart( "One.xml" ) );
            config = definitionFile.Element( "One" )?.Element( "Plugins" )?.Element( "CommandSample" );
            config.ShouldNotBeNull().Value.ShouldBe( "This will work but does nothing." );
        }

        // ckli plugin remove CommandSample
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "remove", "CommandSample" )).ShouldBeTrue();

        definitionFile = XDocument.Load( context.CurrentStackPath.AppendPart( "One.xml" ) );
        config = definitionFile.Element( "One" )?.Element( "Plugins" )?.Element( "CommandSample" );
        config.ShouldBeNull();
    }

    [Test]
    public async Task VSSolutionSample_issues_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.UseReadOnly( "WithIssues" );

        // ckli clone file:///.../WithIssues-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();

        // cd WithIssues
        context = context.ChangeDirectory( "WithIssues" );

        TestEnv.EnsurePluginPackage( "CKli.VSSolutionSample.Plugin" );

        var display = (StringScreen)context.Screen;
        // ckli plugin add VSSolutionSample@version
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "add", $"VSSolutionSample@{TestEnv.CKliPluginsCoreVersion}" )).ShouldBeTrue();

        display.Clear();
        // ckli issue
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > EmptySolution (1)
            │ > ✋ Empty solution file.
            │ │ Ignoring 2 projects:
            │ │ CodeCakeBuilder\CodeCakeBuilder.csproj, SomeJsApp\SomeJsApp.esproj
            > MissingSolution (1)
            │ > ✋ No solution found. Expecting 'MissingSolution.sln' (or '.slnx').
            > MultipleSolutions (1)
            │ > ✋ Multiple solution files found. One of them must be 'MultipleSolutions.sln' (or '.slnx').
            │ │ Found: 'Candidate1.slnx', 'Candidate2.sln', 'SomeOther.slnx'.
            ❰✓❱

            """ );

        // cd WithIssues
        context = context.ChangeDirectory( "MultipleSolutions" );
        // Rename "MultipleSolutions/Candidate2.sln" to "MultipleSolutions/MultipleSolutions.sln".
        // ==> There is no more issue.
        File.Move( context.CurrentDirectory.AppendPart( "Candidate2.sln" ),
                   context.CurrentDirectory.AppendPart( "MultipleSolutions.sln" ) );

        display.Clear();
        // ckli issue
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );

        // cd ..
        context = context.ChangeDirectory( ".." );
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > EmptySolution (1)
            │ > ✋ Empty solution file.
            │ │ Ignoring 2 projects:
            │ │ CodeCakeBuilder\CodeCakeBuilder.csproj, SomeJsApp\SomeJsApp.esproj
            > MissingSolution (1)
            │ > ✋ No solution found. Expecting 'MissingSolution.sln' (or '.slnx').
            ❰✓❱
            
            """ );

    }

}
