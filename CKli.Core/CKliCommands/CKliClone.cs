using CK.Core;
using System;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Clone command.
/// </summary>
sealed class CKliClone : Command
{
    internal CKliClone()
        : base( null,
                "clone",
                "Clones a Stack and all its current World repositories in the current directory.",
                [("stackUrl", "The url stack repository to clone from. The repository name must end with '-Stack'.")],
                [],
                [
                    (["--private"], "Indicates a private repository. A Personal Access Token (or any other secret) is required."),
                    (["--allow-duplicate"], "Allows a Stack that already exists locally to be cloned."),
                    (["--ignore-parent-stack"], "Allows the cloned Stack to be inside an existing one."),
                ] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        string sUrl = cmdLine.EatArgument();
        if( !Uri.TryCreate( sUrl, UriKind.Absolute, out var uri ) )
        {
            monitor.Error( $"Invalid <stackUrl> argument '{sUrl}'. It must be an absolute url." );
            return ValueTask.FromResult( false );
        }
        bool p = cmdLine.EatFlag( "--private" );
        bool a = cmdLine.EatFlag( "--allow-duplicate" );
        bool i = cmdLine.EatFlag( "--ignore-parent-stack" );
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        using( var stack = StackRepository.Clone( monitor, context, uri, !p, a, i ) )
        {
            return ValueTask.FromResult( stack != null );
        }
    }
}
