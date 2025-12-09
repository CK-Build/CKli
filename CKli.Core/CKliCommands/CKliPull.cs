using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPull : Command
{
    public CKliPull()
        : base( null,
                "pull",
                "Resynchronizes the current Repo or World from the remote 'origin'.",
                [],
                [],
                [
                    (["--all"], "Pull all the Repos of the current World (even if current path is in a Repo)."),
                    (["--skip-pull-stack"], "Don't pull the Stack repository itself.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool skipPullStack = cmdLine.EatFlag( "--skip-pull-stack" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && Pull( monitor, context, all, skipPullStack ) );


        static bool Pull( IActivityMonitor monitor, CKliEnv context, bool all, bool skipPullStack )
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
                var repos = all
                            ? world.GetAllDefinedRepo( monitor )
                            : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
                if( repos == null ) return false;
                bool success = true;
                foreach( var repo in repos )
                {
                    success &= repo.Pull( monitor ).IsSuccess();
                }
                // Consider that the final result requires no error when saving a dirty World's DefinitionFile.
                return stack.Close( monitor );
            }
            finally
            {
                // On error, don't save a dirty World's DefinitionFile.
                stack.Dispose();
            }
        }
    }
}
