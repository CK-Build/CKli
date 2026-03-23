using NUnit.Framework;
using Shouldly;
using System.Linq;

namespace CKli.Core.Tests;

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
