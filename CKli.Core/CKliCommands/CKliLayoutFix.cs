using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliLayoutFix : Command
{
    public CKliLayoutFix()
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
                                     && LayoutFix( monitor, context, deleteAliens ) );
    }

    static bool LayoutFix( IActivityMonitor monitor,
                           CKliEnv context,
                           bool deleteAliens = false )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
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
