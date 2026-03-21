using CK.Core;
using CKli.Core;
using CKli.Core.Completion;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Core.Tests;

[TestFixture]
public class CompletionManifestTests
{
    internal static CommandNamespace BuildTestNamespace()
    {
        var cmds = new Dictionary<string, Command?>();
        cmds.Add( "fix", null );
        cmds.Add( "fix start", new TestCommand( "fix start", "Start a fix.",
            arguments: [("version", "The version to fix.")],
            options: [],
            flags: [(["--move-branch"], "Allow branches to be moved.")] ) );
        cmds.Add( "fix info", new TestCommand( "fix info", "Dump fix info.",
            arguments: [], options: [], flags: [] ) );
        cmds.Add( "build", new TestCommand( "build", "Build packages.",
            arguments: [],
            options: [(["--branch", "-b"], "Specify branch.", false)],
            flags: [(["--all"], "Build all."), (["--dry-run", "-d"], "Dry run.")] ) );
        cmds.Add( "*build", new TestCommand( "*build", "Build consumers.",
            arguments: [], options: [], flags: [] ) );
        var nsDescs = new Dictionary<string, string> { ["fix"] = "Fix commands." };
        return CommandNamespace.UnsafeCreate( cmds, nsDescs );
    }

    [Test]
    public void manifest_write_produces_expected_tsv()
    {
        var ns = BuildTestNamespace();
        var tsv = CompletionManifest.Write( ns, CKliCommands.Globals );

        tsv.ShouldContain( "# Global" );
        tsv.ShouldContain( "# Namespaces" );
        tsv.ShouldContain( "# Commands" );
        tsv.ShouldContain( "# Options" );

        // Global section
        tsv.ShouldContain( "--help|-h|-?" );

        // Namespace with description
        tsv.ShouldContain( "\nfix\tFix commands.\n" );

        // Commands with argcount
        tsv.ShouldContain( "fix start\tStart a fix.\t1\n" );
        tsv.ShouldContain( "build\tBuild packages.\t0\n" );
        tsv.ShouldContain( "*build\tBuild consumers.\t0\n" );

        // Options with pipe-separated aliases
        tsv.ShouldContain( "build\t--branch|-b\tSpecify branch.\toption\n" );
        tsv.ShouldContain( "build\t--all\tBuild all.\tflag\n" );
        tsv.ShouldContain( "build\t--dry-run|-d\tDry run.\tflag\n" );
        tsv.ShouldContain( "fix start\t--move-branch\tAllow branches to be moved.\tflag\n" );
    }

    [Test]
    public void manifest_round_trip()
    {
        var ns = BuildTestNamespace();
        var tsv = CompletionManifest.Write( ns, CKliCommands.Globals );
        var data = CompletionManifest.Read( tsv );

        data.Namespaces.ContainsKey( "fix" ).ShouldBeTrue();
        data.Namespaces["fix"].ShouldBe( "Fix commands." );
        data.Commands.ContainsKey( "build" ).ShouldBeTrue();
        data.Commands["build"].Description.ShouldBe( "Build packages." );
        data.Commands["build"].ArgCount.ShouldBe( 0 );
        data.Commands["fix start"].ArgCount.ShouldBe( 1 );
        data.Commands.ContainsKey( "*build" ).ShouldBeTrue();

        data.Globals.Count.ShouldBeGreaterThan( 0 );
        data.Globals.Any( g => g.Names.Contains( "--help" ) && g.Type == "flag" ).ShouldBeTrue();
        data.Globals.Any( g => g.Names.Contains( "--path" ) && g.Type == "option" ).ShouldBeTrue();

        var buildOpts = data.GetOptionsAndFlags( "build" );
        buildOpts.Any( o => o.Names.Contains( "--branch" ) && o.Names.Contains( "-b" ) && o.Type == "option" ).ShouldBeTrue();
        buildOpts.Any( o => o.Names.Contains( "--all" ) && o.Type == "flag" ).ShouldBeTrue();
        buildOpts.Any( o => o.Names.Contains( "--dry-run" ) && o.Names.Contains( "-d" ) && o.Type == "flag" ).ShouldBeTrue();

        var fixOpts = data.GetOptionsAndFlags( "fix start" );
        fixOpts.Any( o => o.Names.Contains( "--move-branch" ) && o.Type == "flag" ).ShouldBeTrue();
    }

    [Test]
    public void manifest_read_returns_empty_on_null_or_empty()
    {
        CompletionManifest.Read( null ).Commands.Count.ShouldBe( 0 );
        CompletionManifest.Read( "" ).Commands.Count.ShouldBe( 0 );
    }
}

[TestFixture]
public class CompletionDataTests
{
    static CompletionData BuildCompletionData()
    {
        var intrinsic = new Dictionary<string, Command?>();
        intrinsic.Add( "clone", new TestCommand( "clone", "Clone a stack.",
            arguments: [("url", "Stack URL")], options: [], flags: [(["--private"], "Private repo.")] ) );
        intrinsic.Add( "pull", new TestCommand( "pull", "Pull all repos.",
            arguments: [], options: [], flags: [] ) );
        var intrinsicNs = CommandNamespace.UnsafeCreate( intrinsic );

        var pluginNs = CompletionManifestTests.BuildTestNamespace();
        var tsv = CompletionManifest.Write( pluginNs, CKliCommands.Globals );
        var manifest = CompletionManifest.Read( tsv );

        return new CompletionData( intrinsicNs, manifest );
    }

    [Test]
    public void empty_input_suggests_all_top_level_and_globals()
    {
        var data = BuildCompletionData();
        var results = data.GetCompletions( [] );
        results.Any( r => r.Completion == "clone" && r.Type == "command" ).ShouldBeTrue();
        results.Any( r => r.Completion == "pull" && r.Type == "command" ).ShouldBeTrue();
        results.Any( r => r.Completion == "build" && r.Type == "command" ).ShouldBeTrue();
        results.Any( r => r.Completion == "fix" && r.Type == "namespace" ).ShouldBeTrue();
        results.Any( r => r.Completion == "*build" && r.Type == "command" ).ShouldBeTrue();
        results.Any( r => r.Completion == "--help" && r.Type == "global" ).ShouldBeTrue();
        results.Any( r => r.Completion == "--path" && r.Type == "global" ).ShouldBeTrue();
    }

    [Test]
    public void partial_command_suggests_matches()
    {
        var data = BuildCompletionData();
        var results = data.GetCompletions( ["cl"] );
        results.Any( r => r.Completion == "clone" ).ShouldBeTrue();
        results.Any( r => r.Completion == "pull" ).ShouldBeFalse();
    }

    [Test]
    public void namespace_suggests_children()
    {
        var data = BuildCompletionData();
        var results = data.GetCompletions( ["fix"] );
        results.Any( r => r.Completion == "start" ).ShouldBeTrue();
        results.Any( r => r.Completion == "info" ).ShouldBeTrue();
    }

    [Test]
    public void matched_command_suggests_flags_with_type()
    {
        var data = BuildCompletionData();
        var results = data.GetCompletions( ["build", "--"] );
        results.Any( r => r.Completion == "--branch" && r.Type == "option" ).ShouldBeTrue();
        results.Any( r => r.Completion == "--all" && r.Type == "flag" ).ShouldBeTrue();
        results.Any( r => r.Completion == "--dry-run" && r.Type == "flag" ).ShouldBeTrue();
    }

    [Test]
    public void used_flags_are_excluded()
    {
        var data = BuildCompletionData();
        var results = data.GetCompletions( ["build", "--all", "--"] );
        results.Any( r => r.Completion == "--all" ).ShouldBeFalse();
        results.Any( r => r.Completion == "--branch" ).ShouldBeTrue();
    }

    [Test]
    public void option_awaiting_value_returns_nothing()
    {
        var data = BuildCompletionData();
        var results = data.GetCompletions( ["build", "--branch"] );
        results.ShouldBeEmpty();
    }

    [Test]
    public void option_with_value_provided_excludes_aliases()
    {
        var data = BuildCompletionData();
        var results = data.GetCompletions( ["build", "-b", "stable", "--"] );
        results.Any( r => r.Completion == "--branch" ).ShouldBeFalse();
        results.Any( r => r.Completion == "-b" ).ShouldBeFalse();
        results.Any( r => r.Completion == "--all" ).ShouldBeTrue();
    }

    [Test]
    public void global_options_are_suggested()
    {
        var data = BuildCompletionData();
        var results = data.GetCompletions( ["build", "--"] );
        results.Any( r => r.Completion == "--help" && r.Type == "global" ).ShouldBeTrue();
    }

    [Test]
    public void used_global_options_are_excluded()
    {
        var data = BuildCompletionData();
        var results = data.GetCompletions( ["build", "--help", "--"] );
        results.Any( r => r.Completion == "--help" ).ShouldBeFalse();
        results.Any( r => r.Completion == "-h" ).ShouldBeFalse();
    }

    [Test]
    public void null_manifest_gives_intrinsic_only()
    {
        var intrinsic = new Dictionary<string, Command?>();
        intrinsic.Add( "clone", new TestCommand( "clone", "Clone.",
            arguments: [], options: [], flags: [] ) );
        var data = new CompletionData( CommandNamespace.UnsafeCreate( intrinsic ), null );
        var results = data.GetCompletions( [] );
        results.Any( r => r.Completion == "clone" ).ShouldBeTrue();
        results.Any( r => r.Completion == "build" ).ShouldBeFalse();
    }
}

[TestFixture]
public class ShellScriptTests
{
    [Test]
    public void bash_script_registers_completion()
    {
        var script = ShellScripts.Bash();
        script.ShouldContain( "complete -o default -F" );
        script.ShouldContain( "ckli complete" );
        script.ShouldContain( "COMPREPLY" );
    }

    [Test]
    public void zsh_script_uses_grouping()
    {
        var script = ShellScripts.Zsh();
        script.ShouldContain( "compdef" );
        script.ShouldContain( "ckli complete" );
        script.ShouldContain( "_describe" );
    }

    [Test]
    public void fish_script_uses_complete()
    {
        var script = ShellScripts.Fish();
        script.ShouldContain( "complete -c ckli" );
        script.ShouldContain( "ckli complete" );
    }

    [Test]
    public void pwsh_script_uses_completion_result_type()
    {
        var script = ShellScripts.Pwsh();
        script.ShouldContain( "Register-ArgumentCompleter" );
        script.ShouldContain( "ckli complete" );
        script.ShouldContain( "CompletionResult" );
        script.ShouldContain( "ParameterName" );
    }

    [TestCase( "bash" )]
    [TestCase( "zsh" )]
    [TestCase( "fish" )]
    [TestCase( "pwsh" )]
    public void get_by_name_returns_non_null( string shell )
    {
        ShellScripts.Get( shell ).ShouldNotBeNull();
    }

    [Test]
    public void get_unknown_returns_null()
    {
        ShellScripts.Get( "cmd" ).ShouldBeNull();
    }
}

[TestFixture]
public class CommandNamespaceDescriptionTests
{
    [Test]
    public void namespace_descriptions_are_available_from_CommandNamespace()
    {
        var cmds = new Dictionary<string, Command?>();
        cmds.Add( "fix", null );
        cmds.Add( "fix start", new TestCommand( "fix start", "Start.", arguments: [], options: [], flags: [] ) );
        var descs = new Dictionary<string, string> { ["fix"] = "Fix commands." };
        var ns = CommandNamespace.UnsafeCreate( cmds, descs );

        ns.NamespaceDescriptions["fix"].ShouldBe( "Fix commands." );
        ns.NamespaceDescriptions.Count.ShouldBe( 1 );
    }

    [Test]
    public void namespace_builder_first_write_wins()
    {
        var b = new CommandNamespaceBuilder();
        b.AddNamespaceDescription( "fix", "First description." );
        b.AddNamespaceDescription( "fix", "Second description." );
        var ns = b.Build();
        ns.NamespaceDescriptions["fix"].ShouldBe( "First description." );
    }
}

[TestFixture]
public class CompletionCommandTests
{
    [Test]
    public void complete_command_is_registered()
    {
        var cmds = CKliCommands.Commands;
        cmds.Namespace.ContainsKey( "complete" ).ShouldBeTrue();
        cmds.Namespace["complete"].ShouldNotBeNull();
    }

    [Test]
    public void completions_namespace_is_registered()
    {
        var cmds = CKliCommands.Commands;
        cmds.Namespace.ContainsKey( "completions" ).ShouldBeTrue();
        cmds.Namespace["completions"].ShouldBeNull(); // namespace entry
    }

    [Test]
    public void completions_script_command_is_registered()
    {
        var cmds = CKliCommands.Commands;
        cmds.Namespace.ContainsKey( "completions script" ).ShouldBeTrue();
        var cmd = cmds.Namespace["completions script"];
        cmd.ShouldNotBeNull();
        cmd!.Arguments.Length.ShouldBe( 1 );
    }

    [Test]
    public void globals_are_defined_on_CKliCommands()
    {
        CKliCommands.Globals.Length.ShouldBeGreaterThan( 0 );
        CKliCommands.Globals.Any( g => g.Names.Contains( "--help" ) && g.Type == "flag" ).ShouldBeTrue();
        CKliCommands.Globals.Any( g => g.Names.Contains( "--path" ) && g.Type == "option" ).ShouldBeTrue();
        CKliCommands.Globals.Any( g => g.Names.Contains( "--ckli-screen" ) && g.Type == "option" ).ShouldBeTrue();
        CKliCommands.Globals.Any( g => g.Names.Contains( "--version" ) && g.Type == "flag" ).ShouldBeTrue();
        CKliCommands.Globals.Any( g => g.Names.Contains( "--ckli-debug" ) && g.Type == "flag" ).ShouldBeTrue();
    }

    [Test]
    public void intrinsic_namespace_descriptions_are_set()
    {
        var cmds = CKliCommands.Commands;
        cmds.NamespaceDescriptions.ContainsKey( "branch" ).ShouldBeTrue();
        cmds.NamespaceDescriptions["branch"].ShouldNotBeNullOrEmpty();
        cmds.NamespaceDescriptions.ContainsKey( "plugin" ).ShouldBeTrue();
        cmds.NamespaceDescriptions.ContainsKey( "completions" ).ShouldBeTrue();
    }
}

sealed class TestCommand : Command
{
    internal TestCommand( string path, string description,
                          ImmutableArray<(string Name, string Description)> arguments,
                          ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> options,
                          ImmutableArray<(ImmutableArray<string> Names, string Description)> flags )
        : base( null, path, description, arguments, options, flags )
    {
    }

    protected override ValueTask<bool> HandleCommandAsync(
        IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
        => ValueTask.FromResult( true );
}
