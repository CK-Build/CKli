using CKli.Core.Completion;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Core.Tests;

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
