using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPush : Command
{
    public CKliPush()
        : base( null,
                "push",
                "Pushes the current Repo or all the current World's Repos current branches to their remotes.",
                [],
                [],
                [
                    (["--all"], "Push all the Repos of the current World (even if current path is in a Repo)."),
                    (["--stack-only"], "Only push the Stack repository, not the current Repo nor the Repos of the current World."),
                    (["--continue-on-error"], "Push all the Repos even on error. By default the first error stops the push."),
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool stackOnly = cmdLine.EatFlag( "--stack-only" );
        bool continueOnError = cmdLine.EatFlag( "--continue-on-error" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && Push( monitor, context, all, stackOnly, continueOnError ) );
    }

    static bool Push( IActivityMonitor monitor,
                      CKliEnv context,
                      bool all = false,
                      bool stackOnly = false,
                      bool continueOnError = false )
    {
        if( !StackRepository.OpenWorldFromPath( monitor,
                                                context,
                                                out var stack,
                                                out var world,
                                                skipPullStack: false ) )
        {
            return false;
        }
        try
        {
            bool success = true;
            if( !stackOnly )
            {
                var repos = all
                     ? world.GetAllDefinedRepo( monitor )
                     : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
                if( repos == null )
                {
                    success = false;
                }
                else
                {
                    foreach( var r in repos )
                    {
                        success &= r.Push( monitor );
                        if( !success && !continueOnError )
                        {
                            break;
                        }
                    }
                }
            }
            if( success || continueOnError )
            {
                success &= stack.PushChanges( monitor );
            }
            return success;
        }
        finally
        {
            stack.Dispose();
        }
    }
}
