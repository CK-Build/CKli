using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Runtime.CompilerServices;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class GitNewLineSettingsTests
{
    [Test]
    public void the_settings_of_the_machine_are_hidden_from_the_tests()
    {
        var path = TestEnv.EnsureCleanFolder().CurrentDirectory;
        using var r = new Repository( Repository.Init( path ) );
        r.Config.Get<string>( "core.autocrlf" ).ShouldBeNull();
        // The first commit of an empty repository needs an author.
        r.Config.BuildSignature( System.DateTimeOffset.Now ).ShouldNotBeNull();

        TestEnv.SetMachineGitConfig( "[core]\n\tautocrlf = input\n" );
        try
        {
            var e = r.Config.Get<string>( "core.autocrlf" ).ShouldNotBeNull();
            e.Value.ShouldBe( "input" );
            e.Level.ShouldBe( ConfigurationLevel.Global );
        }
        finally
        {
            TestEnv.SetMachineGitConfig( null );
        }
    }

    [TestCase( null, null, false, "\n" )]
    [TestCase( null, null, true, "\r\n" )]
    [TestCase( "true", null, false, "\r\n" )]
    [TestCase( "true", "lf", false, "\r\n" )]
    [TestCase( "True", null, false, "\r\n" )]
    [TestCase( "yes", null, false, "\r\n" )]
    [TestCase( "on", null, false, "\r\n" )]
    [TestCase( "1", null, false, "\r\n" )]
    [TestCase( "false", null, true, "\r\n" )]
    [TestCase( "false", "lf", true, "\n" )]
    [TestCase( "false", "crlf", false, "\r\n" )]
    [TestCase( "input", null, true, "\n" )]
    [TestCase( null, "native", false, "\n" )]
    [TestCase( null, "native", true, "\r\n" )]
    [TestCase( "2", null, false, "\r\n" )]
    [TestCase( "0", null, true, "\r\n" )]
    [TestCase( null, "CRLF", false, "\r\n" )]
    public void working_newline_from_settings( string? autoCrlf, string? eol, bool isWindows, string expected )
    {
        GitNewLineSettings.GetWorkingNewLine( autoCrlf, eol, isWindows ).ShouldBe( expected );
    }

    static Repository CreateRepository( string? key = null, string? value = null, bool isBare = false, [CallerMemberName] string? name = null )
    {
        var path = TestEnv.EnsureCleanFolder( name ).CurrentDirectory;
        var r = new Repository( Repository.Init( path, isBare ) );
        if( key != null ) r.Config.Set( key, value, ConfigurationLevel.Local );
        return r;
    }

    [Test]
    public void no_setting_is_right_on_both_platforms()
    {
        using var r = CreateRepository();
        GitNewLineSettings.Check( TestHelper.Monitor, r, isWindows: false ).ShouldBeNull();
        GitNewLineSettings.Check( TestHelper.Monitor, r, isWindows: true ).ShouldBeNull();
    }

    [Test]
    public void local_autocrlf_true_is_wrong_on_linux()
    {
        using var r = CreateRepository( "core.autocrlf", "true" );
        GitNewLineSettings.Check( TestHelper.Monitor, r, isWindows: false )
            .ShouldBe( new GitNewLineProblem( "core.autocrlf", "true", ConfigurationLevel.Local, "\r\n" ) );
    }

    [Test]
    public void local_autocrlf_true_is_right_on_windows()
    {
        using var r = CreateRepository( "core.autocrlf", "true" );
        GitNewLineSettings.Check( TestHelper.Monitor, r, isWindows: true ).ShouldBeNull();
    }

    [Test]
    public void local_autocrlf_input_is_wrong_on_windows()
    {
        using var r = CreateRepository( "core.autocrlf", "input" );
        GitNewLineSettings.Check( TestHelper.Monitor, r, isWindows: true )
            .ShouldBe( new GitNewLineProblem( "core.autocrlf", "input", ConfigurationLevel.Local, "\n" ) );
    }

    [Test]
    public void local_eol_crlf_is_wrong_on_linux()
    {
        using var r = CreateRepository( "core.eol", "crlf" );
        GitNewLineSettings.Check( TestHelper.Monitor, r, isWindows: false )
            .ShouldBe( new GitNewLineProblem( "core.eol", "crlf", ConfigurationLevel.Local, "\r\n" ) );
    }

    [Test]
    public void global_setting_is_reported_with_its_level()
    {
        using var r = CreateRepository();
        TestEnv.SetMachineGitConfig( "[core]\n\tautocrlf = true\n" );
        try
        {
            GitNewLineSettings.Check( TestHelper.Monitor, r, isWindows: false )
                .ShouldBe( new GitNewLineProblem( "core.autocrlf", "true", ConfigurationLevel.Global, "\r\n" ) );
        }
        finally
        {
            TestEnv.SetMachineGitConfig( null );
        }
    }

    [Test]
    public void a_local_setting_wins_over_the_global_one()
    {
        using var r = CreateRepository( "core.autocrlf", "false" );
        TestEnv.SetMachineGitConfig( "[core]\n\tautocrlf = true\n" );
        try
        {
            GitNewLineSettings.Check( TestHelper.Monitor, r, isWindows: false ).ShouldBeNull();
        }
        finally
        {
            TestEnv.SetMachineGitConfig( null );
        }
    }

    [Test]
    public void a_bare_repository_is_skipped()
    {
        using var r = CreateRepository( "core.autocrlf", "true", isBare: true );
        GitNewLineSettings.Check( TestHelper.Monitor, r, isWindows: false ).ShouldBeNull();
    }

    [Test]
    public void an_unreadable_configuration_is_skipped()
    {
        using var r = CreateRepository();
        File.WriteAllText( Path.Combine( r.Info.Path, "config" ), "[core" );
        GitNewLineSettings.Check( TestHelper.Monitor, r, isWindows: false ).ShouldBeNull();
    }

    [Test]
    public void a_key_without_value_is_true()
    {
        using var r = CreateRepository();
        TestEnv.SetMachineGitConfig( "[core]\n\tautocrlf\n" );
        try
        {
            GitNewLineSettings.Check( TestHelper.Monitor, r, isWindows: false )
                .ShouldBe( new GitNewLineProblem( "core.autocrlf", "true", ConfigurationLevel.Global, "\r\n" ) );
        }
        finally
        {
            TestEnv.SetMachineGitConfig( null );
        }
    }

    [TestCase( "", "", true )]
    [TestCase( "a\nb\n", "a\nb\n", true )]
    [TestCase( "a\r\nb\r\n", "a\nb\n", true )]
    [TestCase( "a\nb\n", "a\r\nb\r\n", true )]
    [TestCase( "a\r\nb\n", "a\nb\r\n", true )]
    [TestCase( "a\nb\n", "a\nb", false )]
    [TestCase( "a\nb\n", "a\nc\n", false )]
    [TestCase( "a\rb", "ab", false )]
    [TestCase( "a\rb", "a\nb", false )]
    [TestCase( "a\r\n", "a\r", false )]
    [TestCase( "\uFEFFa\n", "a\n", false )]
    [TestCase( "a\0\r\nb", "a\0\nb", false )]
    [TestCase( "a\0\r\nb", "a\0\r\nb", true )]
    public void same_content_whatever_the_newlines( string a, string b, bool expected )
    {
        var bA = System.Text.Encoding.UTF8.GetBytes( a );
        var bB = System.Text.Encoding.UTF8.GetBytes( b );
        GitNewLineSettings.HasSameContent( bA, bB ).ShouldBe( expected );
        GitNewLineSettings.HasSameContent( bB, bA ).ShouldBe( expected );
    }
}
