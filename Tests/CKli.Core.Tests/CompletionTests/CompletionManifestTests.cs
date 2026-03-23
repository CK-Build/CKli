using CKli.Core.Completion;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

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
