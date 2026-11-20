using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class GitNewLineIssueTests
{
    // The value of "core.autocrlf" that gives the wrong newline on the platform that runs the tests.
    static readonly string _wrong = OperatingSystem.IsWindows() ? "input" : "true";

    static async Task<CKliEnv> CloneCKtAsync( [CallerMemberName] string? name = null )
    {
        var context = TestEnv.EnsureCleanFolder( name );
        var remotes = TestEnv.OpenRemotes( "CKt" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        return context.ChangeDirectory( "CKt" );
    }

    static void SetLocalAutoCrlf( CKliEnv context )
    {
        using var r = new Repository( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core" ) );
        r.Config.Set( "core.autocrlf", _wrong, ConfigurationLevel.Local );
    }

    [Test]
    public async Task no_issue_when_the_settings_are_right_Async()
    {
        var context = await CloneCKtAsync();
        var display = (StringScreen)context.Screen;
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldNotContain( "line endings" );
    }

    [Test]
    public async Task a_machine_setting_gives_one_issue_for_the_world_Async()
    {
        var context = await CloneCKtAsync();
        var display = (StringScreen)context.Screen;
        display.Clear();
        TestEnv.SetMachineGitConfig( $"[core]\n\tautocrlf = {_wrong}\n" );
        try
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        }
        finally
        {
            TestEnv.SetMachineGitConfig( null );
        }
        var text = display.ToString();
        Regex.Matches( text, "Git checks out" ).Count.ShouldBe( 1 );
        text.ShouldContain( $"(Global 'core.autocrlf = {_wrong}')" );
        text.ShouldContain( "Concerns 2 repositories." );
        text.ShouldContain( "git config --global --unset core.autocrlf" );
    }

    [Test]
    public async Task a_repository_setting_gives_an_issue_for_that_repository_Async()
    {
        var context = await CloneCKtAsync();
        var display = (StringScreen)context.Screen;
        display.Clear();
        SetLocalAutoCrlf( context );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        var text = display.ToString();
        text.ShouldContain( $"(Local 'core.autocrlf = {_wrong}')" );
        text.ShouldContain( "config --local --unset core.autocrlf" );
    }

    [Test]
    public async Task fix_does_not_change_the_setting_Async()
    {
        var context = await CloneCKtAsync();
        SetLocalAutoCrlf( context );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue", "--fix" )).ShouldBeTrue();
        using var r = new Repository( context.CurrentDirectory.Combine( "CK-Core-Projects/CKt-Core" ) );
        r.Config.Get<string>( "core.autocrlf", ConfigurationLevel.Local ).Value.ShouldBe( _wrong );
    }
}
