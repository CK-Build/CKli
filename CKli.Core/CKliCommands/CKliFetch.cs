using CK.Core;
using CKli.Core;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliFetch : Command
{
    public CKliFetch()
        : base( null,
                "fetch",
                """
                Fetches all branches (and optionally tags) from the remote(s).
                When --with-tags is specified, locally modified tags are lost.
                """,
                [],
                [],
                [
                    (["--all"], "Fetch from all the Repos of the current World (even if current path is in a Repo)."),
                    (["--with-tags"], "Fetch tags: locally modified tags are lost.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool withTags = cmdLine.EatFlag( "--with-tags" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && Fetch( monitor, this, context, all, withTags, scopeAlive ) );
    }

    static bool Fetch( IActivityMonitor monitor, Command command, CKliEnv context, bool all, bool withTags, CancellationToken scopeAlive )
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
            world.SetExecutingCommand( command, scopeAlive );
            var repos = all
                        ? world.GetAllDefinedRepo( monitor )
                        : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
            if( repos == null ) return false;
            bool success = true;
            foreach( var repo in repos )
            {
                if( scopeAlive.IsCancellationRequested )
                {
                    success = false;
                    break;
                }
                success &= repo.GitRepository.FetchRemoteBranches( monitor, withTags );
            }
            return stack.Close( monitor ) && success;
        }
        finally
        {
            // On unhandled exception, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }
}
