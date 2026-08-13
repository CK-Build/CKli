using CK.Core;
using System;
using System.Threading;
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
                    (["--max-dop"], "Limits the parallelism when cloning the repositories."),
                ] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override async ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                          CKliEnv context,
                                                                          CommandLineArguments cmdLine,
                                                                          CancellationToken scopeAlive )
    {
        string sUrl = cmdLine.EatArgument();
        if( !Uri.TryCreate( sUrl, UriKind.Absolute, out var uri ) )
        {
            monitor.Error( $"Invalid <stackUrl> argument '{sUrl}'. It must be an absolute url." );
            return false;
        }
        bool isPrivate = cmdLine.EatFlag( "--private" );
        bool allowDuplicate = cmdLine.EatFlag( "--allow-duplicate" );
        bool ignoreParentStack = cmdLine.EatFlag( "--ignore-parent-stack" );
        if( !PluginBase.ParseInteger( monitor,
                                      "--max-dop",
                                      cmdLine.EatSingleOption( "--max-dop" ),
                                      out int maxDop,
                                      defaultValue: 0,
                                      minValue: 1 ) )
        {
            return false;
        }
        if( !cmdLine.Close( monitor ) )
        {
            return false;
        }
        using( var stack = await StackRepository.CloneAsync( monitor,
                                                             context,
                                                             uri,
                                                             !isPrivate,
                                                             allowDuplicate,
                                                             ignoreParentStack,
                                                             "main",
                                                             maxDop,
                                                             scopeAlive ) )
        {
            return stack != null;
        }
    }
}
