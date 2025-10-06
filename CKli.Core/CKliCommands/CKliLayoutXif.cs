using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliLayoutXif : Command
{
    public CKliLayoutXif()
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
                                                                    CommandCommonContext context,
                                                                    CommandLineArguments cmdLine )
    {
        return ValueTask.FromResult( cmdLine.CheckNoRemainingArguments( monitor )
                                     && LayoutXif( monitor, context ) );
    }

    static bool LayoutXif( IActivityMonitor monitor, CommandCommonContext context )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            return world.XifLayout( monitor );
        }
        finally
        {
            stack.Dispose();
        }
    }
}
