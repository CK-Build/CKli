using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPull : CommandDescription
{
    public CKliPull()
        : base( null,
                "pull",
                "Resynchronizes the current Repo or World from the remotes.",
                [],
                [],
                [
                    (["--all"], "Pull all the Repos of the current World (even if current path is in a Repo)."),
                    (["--skip-pull-stack"], "Don't pull the Stack repository itself.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CommandCommonContext context,
                                                                    CommandLineArguments cmdLine )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool skipPullStack = cmdLine.EatFlag( "--skip-pull-stack" );
        return ValueTask.FromResult( cmdLine.CheckNoRemainingArguments( monitor )
                                     && Pull( monitor, context, all, skipPullStack ) );


        static bool Pull( IActivityMonitor monitor, CommandCommonContext context, bool all, bool skipPullStack )
        {
            if( !StackRepository.OpenWorldFromPath( monitor,
                                                    context,
                                                    out var stack,
                                                    out var world,
                                                    skipPullStack ) )
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
                        return repo.Pull( monitor ).IsSuccess();
                    }
                }
                return world.Pull( monitor );
            }
            finally
            {
                stack.Dispose();
            }
        }
    }
}
