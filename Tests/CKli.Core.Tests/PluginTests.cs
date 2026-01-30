using CK.Core;
using CKli.Core.GitHosting.Providers;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
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
        var remotes = TestEnv.OpenRemotes( "One" );
        var display = (StringScreen)context.Screen;

        // ckli clone file:///.../One-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd One
        context = context.ChangeDirectory( "One" );

        // ckli plugin create MyFirstOne
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "create", "MyFirstOne" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            display.Clear();
            // ckli plugin info
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "info" )).ShouldBeTrue();

            logs.ShouldContain( "New 'MyFirstOne' in world 'One' plugin certainly requires some development." );

            display.ToString().ShouldBe( """
                1 loaded plugins, 1 configured plugins.

                > MyFirstOne     > <MyFirstOne />
                │    Available   │ 
                │    <source>    │ 
                │ Message:
                │    Message from 'MyFirstOne' plugin.
                ❰✓❱

                """ );
        }

        display.Clear();
        // ckli plugin info --compile-mode debug
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "info", "--compile-mode", "debug" )).ShouldBeTrue();

        display.ToString().ShouldBe( """
            1 loaded plugins, 1 configured plugins. (CompileMode: Debug)

            > MyFirstOne     > <MyFirstOne />
            │    Available   │ 
            │    <source>    │ 
            │ Message:
            │    Message from 'MyFirstOne' plugin.
            ❰✓❱

            """ );

        // ckli plugin create MySecondOneWithLongName
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "create", "MySecondOneWithLongName" )).ShouldBeTrue();

        display.Clear();
        // ckli plugin info
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "info" )).ShouldBeTrue();
        // We don't have a ColSpan capability in TableLayout yet. Here the Message should be in a "cell" with ColSpan: 2.
        display.ToString().ShouldBe( """
            2 loaded plugins, 2 configured plugins. (CompileMode: Debug)

            > MyFirstOne     > <MyFirstOne />
            │    Available   │ 
            │    <source>    │ 
            │ Message:
            │    Message from 'MyFirstOne' plugin.
            > MySecondOneWithLongName   > <MySecondOneWithLongName />
            │    Available              │ 
            │    <source>               │ 
            │ Message:
            │    Message from 'MySecondOneWithLongName' plugin.
            ❰✓❱

            """ );

        // ckli plugin remove MyFirstOne
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "remove", "MyFirstOne" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            display.Clear();
            // ckli plugin info
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "info" )).ShouldBeTrue();
            logs.ShouldNotContain( "New 'MyFirstOne' in world 'One' plugin certainly requires some development." );

            display.ToString().ShouldBe( """
                1 loaded plugins, 1 configured plugins. (CompileMode: Debug)

                > MySecondOneWithLongName   > <MySecondOneWithLongName />
                │    Available              │ 
                │    <source>               │ 
                │ Message:
                │    Message from 'MySecondOneWithLongName' plugin.
                ❰✓❱

                """ );
        }
    }

    [Test]
    public async Task CommandSample_package_echo_and_CmdLine_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "One" );

        TestEnv.EnsurePluginPackage( "CKli.CommandSample.Plugin" );

        // ckli clone file:///.../One-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd One
        context = context.ChangeDirectory( "One" );

        // ckli plugin add CommandSample@version
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "add", $"CommandSample@{TestEnv.CKliPluginsCoreVersion}" )).ShouldBeTrue();

        // Echo test (compiled)
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "echo", "hello n°1" )).ShouldBeTrue();
            logs.ShouldContain( $"echo: hello n°1 - {context.StartCommandHandlingLocalTime}" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "echo", "hello n°2", "--upper-case" )).ShouldBeTrue();
            logs.ShouldContain( $"echo: HELLO N°2 - {context.StartCommandHandlingLocalTime}" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "get-world-name" )).ShouldBeTrue();
            logs.ShouldContain( "get-world-name: One" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "get-world-name", "-l" )).ShouldBeTrue();
            logs.ShouldContain( "get-world-name: one" );
        }

        // CommandLineArguments or no argument tests (compiled)
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "no-argument-at-all" )).ShouldBeTrue();
            logs.ShouldContain( $"CommandWithNoArgument!!!" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "cklienv-only" )).ShouldBeTrue();
            logs.ShouldContain( $"CommandWithCKliEnvOnly!!!" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line" )).ShouldBeTrue();
            logs.ShouldContain( "CommandWithCommandLineArgumentsOnly!!!" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line-env" )).ShouldBeTrue();
            logs.ShouldContain( "CommandWithCommandLineArgumentsAndCKliEnv!!!" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-env-line" )).ShouldBeTrue();
            logs.ShouldContain( "CommandWithCKliEnvAndCommandLineArguments!!!" );

            // Forgetting to close the CommandLineArguments is an error.
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line", "--forget-close" )).ShouldBeFalse();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line-env", "--forget-close" )).ShouldBeFalse();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-env-line", "--forget-close" )).ShouldBeFalse();
            // Too much (unexpected) arguments is also an error.
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line", "--some" )).ShouldBeFalse();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line-env", "more" )).ShouldBeFalse();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-env-line", "-args a" )).ShouldBeFalse();
        }

        // ckli plugin info --compile-mode none
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "info", "--compile-mode", "none" )).ShouldBeTrue();

        // Echo test (via reflection)
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "echo", "hello n°1" )).ShouldBeTrue();
            logs.ShouldContain( $"echo: hello n°1 - {context.StartCommandHandlingLocalTime}" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "echo", "hello n°2", "--upper-case" )).ShouldBeTrue();
            logs.ShouldContain( $"echo: HELLO N°2 - {context.StartCommandHandlingLocalTime}" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "get-world-name" )).ShouldBeTrue();
            logs.ShouldContain( "get-world-name: One" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "get-world-name", "-l" )).ShouldBeTrue();
            logs.ShouldContain( "get-world-name: one" );

            // Forgetting to close the CommandLineArguments is an error.
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line", "--forget-close" )).ShouldBeFalse();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line-env", "--forget-close" )).ShouldBeFalse();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-env-line", "--forget-close" )).ShouldBeFalse();
            // Too much (unexpected) arguments is also an error.
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line", "--some" )).ShouldBeFalse();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line-env", "more" )).ShouldBeFalse();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-env-line", "-args a" )).ShouldBeFalse();
        }

        // CommandLineArguments or no argument tests (via reflection)
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "no-argument-at-all" )).ShouldBeTrue();
            logs.ShouldContain( $"CommandWithNoArgument!!!" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "cklienv-only" )).ShouldBeTrue();
            logs.ShouldContain( $"CommandWithCKliEnvOnly!!!" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line" )).ShouldBeTrue();
            logs.ShouldContain( "CommandWithCommandLineArgumentsOnly!!!" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-line-env" )).ShouldBeTrue();
            logs.ShouldContain( "CommandWithCommandLineArgumentsAndCKliEnv!!!" );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "command-env-line" )).ShouldBeTrue();
            logs.ShouldContain( "CommandWithCKliEnvAndCommandLineArguments!!!" );
        }

    }

    [Test]
    public async Task CommandSample_package_config_edit_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "One" );

        TestEnv.EnsurePluginPackage( "CKli.CommandSample.Plugin" );

        // ckli clone file:///.../One-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        // cd One
        context = context.ChangeDirectory( "One" );

        // ckli plugin add CommandSample@version
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "add", $"CommandSample@{TestEnv.CKliPluginsCoreVersion}" )).ShouldBeTrue();

        await TestPluginConfigurationAsync( context );
        await TestPluginConfigurationForRepoAsync( context, "OneRepo" );

        // ckli plugin remove CommandSample
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "remove", "CommandSample" )).ShouldBeTrue();

        ReadConfigElement( context ).ShouldBeNull();
        ReadRepoConfigElement( context, "OneRepo" ).ShouldBeNull();

        static async Task TestPluginConfigurationAsync( CKliEnv context )
        {
            var config = ReadConfigElement( context );
            config.ShouldNotBeNull().Value.ShouldBe( "Initial Description..." );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "config", "edit", "New Description!" )).ShouldBeTrue();

            config = ReadConfigElement( context );
            config.ShouldNotBeNull().Value.ShouldBe( "New Description!" );

            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "config", "edit", "Will fail!", "--remove-plugin-configuration" ))
                    .ShouldBeFalse();

                logs.ShouldContain( """
                    Plugin 'CommandSample' error while editing configuration:
                    <CommandSample>Will fail!</CommandSample>
                    """ );

                config = ReadConfigElement( context );
                config.ShouldNotBeNull().Value.ShouldBe( "New Description!", "Definition file is not saved on error." );
            }

            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "test", "config", "edit", "This will work but does nothing.", "--rename-plugin-configuration" ))
                .ShouldBeTrue();

            config = ReadConfigElement( context );
            config.ShouldNotBeNull().Value.ShouldBe( "This will work but does nothing." );
        }

        static async Task TestPluginConfigurationForRepoAsync( CKliEnv context, string repoName )
        {
            XElement? repoConfig = ReadRepoConfigElement( context, repoName );
            repoConfig.ShouldBeNull();

            (await CKliCommands.ExecAsync( TestHelper.Monitor,
                                            context,
                                            "test", "config", "edit", "For OneRepo!",
                                            "--repo-name", repoName ))
                .ShouldBeTrue();

            repoConfig = ReadRepoConfigElement( context, repoName );
            repoConfig.ShouldNotBeNull().Value.ShouldBe( "For OneRepo!" );

            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                (await CKliCommands.ExecAsync( TestHelper.Monitor,
                                               context,
                                               "test", "config", "edit", "Will fail!", "--remove-plugin-configuration",
                                               "--repo-name", repoName ))
                    .ShouldBeFalse();

                logs.ShouldContain( """
                    Plugin 'CommandSample' error while editing configuration for 'OneRepo':
                    <CommandSample>Will fail!</CommandSample>
                    """ );

                repoConfig = ReadRepoConfigElement( context, repoName );
                repoConfig.ShouldNotBeNull().Value.ShouldBe( "For OneRepo!", "Definition file is not saved on error." );
            }

            (await CKliCommands.ExecAsync( TestHelper.Monitor,
                                            context,
                                            "test", "config", "edit", "This will work but does nothing.", "--rename-plugin-configuration",
                                            "--repo-name", repoName ))
                .ShouldBeTrue();

            repoConfig = ReadRepoConfigElement( context, repoName );
            repoConfig.ShouldNotBeNull().Value.ShouldBe( "This will work but does nothing." );

            // Testing RemoveConfigurationFor( repo ).
            (await CKliCommands.ExecAsync( TestHelper.Monitor,
                                            context,
                                            "test", "config", "edit", "Removed...", "--repo-configuration-remove",
                                            "--repo-name", repoName ))
                .ShouldBeTrue();

            repoConfig = ReadRepoConfigElement( context, repoName );
            repoConfig.ShouldBeNull();

            // Adds the configuration back to be able to test Plugin remove (that removes the per Repo plugin configurations).
            (await CKliCommands.ExecAsync( TestHelper.Monitor,
                                context,
                                "test", "config", "edit", "Will be removed by 'ckli plugin remove'.",
                                "--repo-name", repoName ))
                    .ShouldBeTrue();

            repoConfig = ReadRepoConfigElement( context, repoName );
            repoConfig.ShouldNotBeNull().Value.ShouldBe( "Will be removed by 'ckli plugin remove'." );

        }

        static XElement? ReadConfigElement( CKliEnv context )
        {
            var definitionFile = XDocument.Load( context.CurrentStackPath.AppendPart( "One.xml" ) );
            return definitionFile.Element( "One" )?.Element( "Plugins" )?.Element( "CommandSample" );
        }

        static XElement? ReadRepoConfigElement( CKliEnv context, string repoName )
        {
            var definitionFile = XDocument.Load( context.CurrentStackPath.AppendPart( "One.xml" ) );
            return definitionFile.Descendants( "Repository" )
                                            .Single( e => e.Attribute( "Url" )!.Value.Contains( repoName ) )
                                            .Element( "CommandSample" );
        }
    }


    [Test]
    public async Task VSSolutionSample_issues_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "WithIssues" );
        var display = (StringScreen)context.Screen;

        // ckli clone file:///.../WithIssues-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();

        // cd WithIssues
        context = context.ChangeDirectory( "WithIssues" );

        TestEnv.EnsurePluginPackage( "CKli.VSSolutionSample.Plugin" );

        // ckli plugin add VSSolutionSample@version
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "add", $"VSSolutionSample@{TestEnv.CKliPluginsCoreVersion}" )).ShouldBeTrue();

        display.Clear();
        // ckli issue
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().Replace('\\','/').ShouldBe( """
            > EmptySolution (1)
            │ > ✋ Empty solution file.
            │ │ Ignoring 2 projects:
            │ │ CodeCakeBuilder/CodeCakeBuilder.csproj, SomeJsApp/SomeJsApp.esproj
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
        display.ToString().Replace('\\','/').ShouldBe( """
            > EmptySolution (1)
            │ > ✋ Empty solution file.
            │ │ Ignoring 2 projects:
            │ │ CodeCakeBuilder/CodeCakeBuilder.csproj, SomeJsApp/SomeJsApp.esproj
            > MissingSolution (1)
            │ > ✋ No solution found. Expecting 'MissingSolution.sln' (or '.slnx').
            ❰✓❱

            """ );

    }

}
