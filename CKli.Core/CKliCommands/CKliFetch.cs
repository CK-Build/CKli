using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliFetch : Command
{
    public CKliFetch()
        : base( null,
                "fetch",
                "Fetches all branches of the current Repo or all the Repos of the current World.",
                [],
                [],
                [
                    (["--all"], "Fetch from all the Repos of the current World (even if current path is in a Repo)."),
                    (["--from-all-remotes"], "Fetch from all available remotes, not only from 'origin'.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool fromAllRemotes = cmdLine.EatFlag( "--from-all-remotes" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && Fetch( monitor, context, all, fromAllRemotes ) );
    }

    static bool Fetch( IActivityMonitor monitor, CKliEnv context, bool all, bool fromAllRemotes )
    {
        if( !StackRepository.OpenWorldFromPath( monitor,
                                                context,
                                                out var stack,
                                                out var world,
                                                skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            if( !all )
            {
                var repo = world.TryGetRepo( monitor, context.CurrentDirectory );
                if( repo != null )
                {
                    return repo.Fetch( monitor, originOnly: !fromAllRemotes );
                }
            }
            return world.Fetch( monitor, originOnly: !fromAllRemotes );
        }
        finally
        {
            stack.Dispose();
        }
    }
}
