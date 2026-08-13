using CK.Core;
using CKli.Core;
using System.Linq;
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
                    (["--with-tags"], "Fetch tags: locally modified tags are lost."),
                    (["--max-dop"], "Limits the parallelism when fetching the repositories."),
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
        if( !PluginBase.ParseInteger( monitor,
                                      "--max-dop",
                                      cmdLine.EatSingleOption( "--max-dop" ),
                                      out int maxDop,
                                      defaultValue: 0,
                                      minValue: 1 )
            || !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        return new ValueTask<bool>( FetchAsync( monitor, this, context, all, withTags, maxDop, scopeAlive ) );
    }

    static async Task<bool> FetchAsync( IActivityMonitor monitor,
                                        Command command,
                                        CKliEnv context,
                                        bool all,
                                        bool withTags,
                                        int maxDop,
                                        CancellationToken scopeAlive )
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

            using( monitor.OpenInfo( $"Fetching {repos.Count} repositories ({(maxDop <= 0 ? "parallel" : $"--max-dop {maxDop}")})." ) )
            {
                var pool = new ActivityMonitorAsyncPool( maxDop <= 0 ? int.MaxValue : maxDop );
                bool success = await pool.ParallelAsync( repos,
                                                         ( monitor, repo, cancellation ) => repo.GitRepository.FetchRemoteBranches( monitor, withTags, cancellation: cancellation ),
                                                         ParallelErrorBehavior.SoftStop,
                                                         scopeAlive )
                                         .ConfigureAwait( false );
                return stack.Close( monitor ) && success;
            }
        }
        finally
        {
            // On unhandled exception, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }
}
