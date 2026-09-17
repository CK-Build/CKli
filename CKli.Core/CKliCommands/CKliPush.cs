using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
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
                    (["--max-dop"], "Limits the parallelism when pulling and pushing the repositories."),
                ],
                summary: "Pushes the Stack repository and all Repo's local branches that track a remote branch." )
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
        return new ValueTask<bool>( PushAsync( monitor, this, context, stackOnly, all, continueOnError, maxDop, scopeAlive ) );
    }

    static async Task<bool> PushAsync( IActivityMonitor monitor,
                                       Command command,
                                       CKliEnv context,
                                       bool stackOnly,
                                       bool all,
                                       bool continueOnError,
                                       int maxDop,
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
                    // first and then push them (CKliPull.DoPullAsync fetches all remote branches and then
                    // merges them).
                    if( await CKliPull.DoPullAsync( monitor, continueOnError, repos, withTags: false, maxDop, scopeAlive )
                                      .ConfigureAwait( false ) )
                    {
                        // Repositories are independent: they are pushed in parallel, exactly like they have
                        // just been pulled. Each of them is pushed by a single network operation (see PushOne).
                        using( monitor.OpenInfo( $"Pushing {repos.Count} repositories ({(maxDop <= 0 ? "parallel" : $"--max-dop {maxDop}")})." ) )
                        {
                            var pool = new ActivityMonitorAsyncPool( maxDop <= 0 ? int.MaxValue : maxDop );
                            // SoftStop: on error, the started repositories end but no new one is pushed.
                            success &= await pool.ParallelAsync( repos,
                                                                 static ( monitor, repo, cancellation ) => PushOne( monitor, repo ),
                                                                 continueOnError ? ParallelErrorBehavior.Ignore : ParallelErrorBehavior.SoftStop,
                                                                 scopeAlive )
                                                 .ConfigureAwait( false );
                        }
                    }
                    else
                    {
                        success = false;
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

    /// <summary>
    /// Pushes all the branches of one repository that track an "origin" branch with a single call to
    /// <see cref="GitRepository.Push"/>: a push is a full connection to the remote and the low level push
    /// handles any number of ref specs at once (and the pending <see cref="GitRepository.DeferredPushRefSpecs"/>).
    /// <para>
    /// This must never be called concurrently for the same <paramref name="repo"/>: a <see cref="GitRepository"/>
    /// is not thread safe. Parallelism here is across repositories only.
    /// </para>
    /// </summary>
    static bool PushOne( IActivityMonitor monitor, Repo repo )
    {
        var git = repo.GitRepository;
        var refSpecs = new List<string>();
        foreach( var b in git.Repository.Branches )
        {
            var tracked = b.TrackedBranch;
            if( tracked == null
                || !tracked.CanonicalName.StartsWith( "refs/remotes/origin/", StringComparison.Ordinal ) )
            {
                continue;
            }
            if( GitRepository.IsLocalOnlyRefName( b.FriendlyName ) )
            {
                // Not an error here: this pushes whatever tracks a remote branch, the user doesn't name them.
                // GitRepository.Push skips such a ref spec anyway: warning here names the branch.
                monitor.Warn( $"Skipping branch '{b.FriendlyName}' of '{repo.DisplayPath}': 'local/' and 'building/' references are never pushed." );
                continue;
            }
            refSpecs.Add( $"{b.CanonicalName}:{b.CanonicalName}" );
        }
        if( refSpecs.Count == 0 && git.DeferredPushRefSpecs.Count == 0 )
        {
            monitor.Trace( $"No branch tracking 'origin' to push in '{repo.DisplayPath}'." );
            return true;
        }
        using( monitor.OpenInfo( $"Pushing branches of '{repo.DisplayPath}'." ) )
        {
            return git.GetRemote( monitor, "origin", forWrite: true, out var remote, out var creds )
                   && git.Push( monitor, remote, creds, refSpecs );
        }
    }
}
