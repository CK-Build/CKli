using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliTagFetch : Command
{
    public CKliTagFetch()
        : base( null,
                "tag fetch",
                """
                Fetches all tags that are only on the remote "origin": this preserves local tags.
                """,
                [],
                [],
                [
                    (["--all"], "Consider all the Repos' of the current World (even if current path is in a Repo).")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool all = cmdLine.EatFlag( "--all" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && FetchTags( monitor, this, context, all ) );
    }

    static bool FetchTags( IActivityMonitor monitor,
                           Command command,
                           CKliEnv context,
                           bool all )
    {
        if( !StackRepository.OpenWorldFromPath( monitor,
                                                context,
                                                out var stack,
                                                out var world,
                                                skipPullStack: true ) )
        {
            return false;
        }
        var s = context.Screen.ScreenType;
        try
        {
            world.SetExecutingCommand( command );
            var repos = all
                        ? world.GetAllDefinedRepo( monitor )
                        : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
            if( repos == null ) return false;
            foreach( var repo in repos )
            {
                if( !repo.GitRepository.FetchTags( monitor ) )
                {
                    return false;
                }
            }
            return true;
        }
        finally
        {
            stack.Dispose();
        }
    }
}
