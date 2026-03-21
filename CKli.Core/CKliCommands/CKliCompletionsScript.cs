using CK.Core;
using CKli.Core.Completion;
using System;
using System.Threading.Tasks;

namespace CKli.Core;

sealed class CKliCompletionsScript : Command
{
    internal CKliCompletionsScript()
        : base( null,
                "completions script",
                "Outputs a shell completion script. Source the output in your shell profile.",
                [("shell", "Shell type: bash, zsh, fish, or pwsh.")],
                [],
                [] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        var shell = cmdLine.EatArgument();
        if( !cmdLine.Close( monitor ) ) return ValueTask.FromResult( false );

        var script = ShellScripts.Get( shell );
        if( script == null )
        {
            monitor.Error( $"Unknown shell '{shell}'. Supported: bash, zsh, fish, pwsh." );
            return ValueTask.FromResult( false );
        }
        Console.Out.Write( script );
        return ValueTask.FromResult( true );
    }
}
