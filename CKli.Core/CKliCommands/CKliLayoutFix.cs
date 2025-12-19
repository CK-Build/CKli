using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Fixes the world layout (if it needs to) and raises the <see cref="WorldEvents.FixedLayout"/>.
/// </summary>
public sealed class CKliLayoutFix : Command
{
    internal CKliLayoutFix()
        : base( null,
                "layout fix",
                "Fixes the the folders and repositories layout of the current world (including casing differences).",
                [],
                [],
                [
                    (["--delete-aliens"], "Delete repositories that don't belong to the current world.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool deleteAliens = cmdLine.EatFlag( "--delete-aliens" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && LayoutFix( monitor, this, context, deleteAliens ) );
    }

    static bool LayoutFix( IActivityMonitor monitor,
                           Command command,
                           CKliEnv context,
                           bool deleteAliens = false )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command );
            // Consider that the final result requires no error when saving a dirty World's DefinitionFile.
            return world.FixLayout( monitor, deleteAliens, out _ ) && stack.Close( monitor );
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }
}
