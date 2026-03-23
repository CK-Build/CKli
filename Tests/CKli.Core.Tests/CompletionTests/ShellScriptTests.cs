using CKli.Core.Completion;
using NUnit.Framework;
using Shouldly;

namespace CKli.Core.Tests;

[TestFixture]
public class ShellScriptTests
{
    [Test]
    public void bash_script_registers_completion()
    {
        var script = ShellScripts.Get( "bash" ).ShouldNotBeNull();
        script.ShouldContain( "complete -o default -F" );
        script.ShouldContain( "ckli complete" );
        script.ShouldContain( "COMPREPLY" );
    }

    [Test]
    public void zsh_script_uses_grouping()
    {
        var script = ShellScripts.Get( "zsh" ).ShouldNotBeNull();
        script.ShouldContain( "compdef" );
        script.ShouldContain( "ckli complete" );
        script.ShouldContain( "_describe" );
    }

    [Test]
    public void fish_script_uses_complete()
    {
        var script = ShellScripts.Get( "fish" ).ShouldNotBeNull();
        script.ShouldContain( "complete -c ckli" );
        script.ShouldContain( "ckli complete" );
    }

    [Test]
    public void pwsh_script_uses_completion_result_type()
    {
        var script = ShellScripts.Get( "pwsh" ).ShouldNotBeNull();
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
