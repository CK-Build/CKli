using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliIssue : Command
{
    public CKliIssue()
        : base( null,
                "issue",
                "Asks plugins to detect any possible issues.",
                arguments: [],
                options: [],
                flags: [] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && Issue( monitor, context ) );
    }

    static bool Issue( IActivityMonitor monitor, CKliEnv context )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            var disabledPlugin = world.GetDisabledPluginsHeader();
            if( disabledPlugin != null )
            {
                monitor.Warn( disabledPlugin );
                return true;
            }
            return world.Events.SafeRaiseEvent( monitor, new IssueEvent( monitor, world ) );
        }
        finally
        {
            stack.Dispose();
        }
    }

}
