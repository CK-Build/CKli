using CK.Core;
using CKli.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPush : Command
{
    public CKliPush()
        : base( null,
                "push",
                """
                Pushes the Stack repository and all Repo's local branches that track a remote branch.
                A pull is done before: it must be successful for the actual push to be done.
                """,
                [],
                [],
                [
                    (["--stack-only"], "Only push the Stack repository, not the Repos."),
                    (["--all"], "Consider all the Repos' of the current World (even if current path is in a Repo)."),
                    (["--continue-on-error"], "Continues even on error. By default the first error stops the operation."),
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        bool stackOnly = cmdLine.EatFlag( "--stack-only" );
        bool all = cmdLine.EatFlag( "--all" );
        bool continueOnError = cmdLine.EatFlag( "--continue-on-error" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && Push( monitor, this, context, stackOnly, all, continueOnError, scopeAlive ) );
    }

    static bool Push( IActivityMonitor monitor,
                      Command command,
                      CKliEnv context,
                      bool stackOnly,
                      bool all,
                      bool continueOnError,
                      CancellationToken scopeAlive )
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
            world.SetExecutingCommand( command, scopeAlive );
            // The Stack has been opened with pull. If this succeeded, we push
            // it immediately.
            bool success = stack.PushChanges( monitor );
            if( !stackOnly && (success || continueOnError) )
            {
                // If we must handle the Repos, we first pull them.
                var repos = all
                     ? world.GetAllDefinedRepo( monitor )
                     : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
                if( repos == null )
                {
                    success = false;
                }
                else
                {
                    // Even if we "continue on error", if pull fails, we don't push.
                    // Instead of working branch per branch, we pull (fetch-merge) all branches
                    // first and then push them (CKliPull.DoPull fetches all remote branches and then
                    // merges them).
                    if( CKliPull.DoPull( monitor, continueOnError, repos, withTags: false, scopeAlive ) )
                    {
                        foreach( var repo in repos )
                        {
                            if( scopeAlive.IsCancellationRequested )
                            {
                                return false;
                            }
                            var r = repo.GitRepository.Repository;
                            foreach( var b in r.Branches )
                            {
                                var tracked = b.TrackedBranch;
                                if( tracked != null
                                    && tracked.CanonicalName.StartsWith( "refs/remotes/origin/", StringComparison.Ordinal ) )
                                {
                                    if( GitRepository.IsLocalOnlyRefName( b.FriendlyName ) )
                                    {
                                        // Not an error here: this pushes whatever tracks a remote branch, the user
                                        // doesn't name them. GitRepository.PushBranch would fail on such a branch.
                                        monitor.Warn( $"Skipping branch '{b.FriendlyName}' of '{repo.DisplayPath}': 'local/' and 'building/' references are never pushed." );
                                        continue;
                                    }
                                    success &= repo.GitRepository.PushBranch( monitor, b, autoCreateRemoteBranch: false );
                                    if( !success && !continueOnError ) break;
                                }
                            }
                        }
                    }
                }
            }
            return stack.Close( monitor ) && success;
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }
}
