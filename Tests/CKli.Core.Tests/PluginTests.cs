using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
using static System.Net.Mime.MediaTypeNames;

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

                > MyFirstOne     <MyFirstOne />
                │    Available   
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
    public async Task add_CommandSample_package_Async()
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

}
