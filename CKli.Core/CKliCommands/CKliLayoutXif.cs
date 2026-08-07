using CK.Core;
using CKli.Core;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliLayoutXif : Command
{
    internal CKliLayoutXif()
        : base( null,
                "layout xif",
                """
                Updates the layout of the current world from existing folders and repositories.
                To share this updated layout with others, 'push --stackOnly' must be executed.
                """,
                [], [], [] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && LayoutXif( monitor, this, context, scopeAlive ) );
    }

    static bool LayoutXif( IActivityMonitor monitor, Command command, CKliEnv context, CancellationToken scopeAlive )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command, scopeAlive );
            // XifLayout handles the WorldDefinition file save and commit.
            return world.XifLayout( monitor );
        }
        finally
        {
            stack.Dispose();
        }
    }
}
