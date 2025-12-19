using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

// Fetch is the way to go (along with manual merges).
// See https://stackoverflow.com/questions/4318161/can-git-pull-all-update-all-my-local-branches
// This is not easy to find a safe global pattern.
sealed class CKliPull : Command
{
    internal CKliPull()
        : base( null,
                "pull",
                """Fetch-Merge the Repo's current head from its tracked remote branch on remote "origin".""",
                [],
                [],
                [
                    (["--all"], "Pulls all the Repos' current head of the current World (even if current path is in a Repo)."),
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
                                     && Pull( monitor, this, context, all, skipPullStack ) );


        static bool Pull( IActivityMonitor monitor, Command command, CKliEnv context, bool all, bool skipPullStack )
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
                world.SetExecutingCommand( command );
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
