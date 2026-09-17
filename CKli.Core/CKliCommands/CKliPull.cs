using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPull : Command
{

    internal CKliPull()
        : base( null,
                "pull",
                """
                Pulls the Stack repository and all Repo's local branches that track a remote branch.
                By default, all remote tags are only "fetched": local tags are preserved. When --with-tags is specified, remote tags overwrite local ones.
                Use 'ckli tag list' to analyze local/remote and conflicting tags.
                """,
                [],
                [],
                [
                    (["--all"], "Consider all the Repos' of the current World (even if current path is in a Repo)."),
                    (["--with-tags"], "Pull tags: remote tags replace local ones with the same name."),
                    (["--continue-on-error"], "Continues even on error. By default the first error stops the operation."),
                    (["--max-dop"], "Limits the parallelism when pulling the repositories."),
             ],
                summary: "Pulls the Stack repository and all Repo's local branches that track a remote branch." )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool withTags = cmdLine.EatFlag( "--with-tags" );
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
        return new ValueTask<bool>( PullAsync( monitor, this, context, all, maxDop, withTags, continueOnError, scopeAlive ) );


        static async Task<bool> PullAsync( IActivityMonitor monitor,
                                           Command command,
                                           CKliEnv context,
                                           bool all,
                                           int maxDop,
                                           bool withTags,
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
                var repos = all
                            ? world.GetAllDefinedRepo( monitor )
                            : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
                if( repos == null ) return false;

                bool success = await DoPullAsync( monitor, continueOnError, repos, withTags, maxDop, scopeAlive )
                                        .ConfigureAwait( false );
                // Save a dirty World's DefinitionFile ony if no unhandled exception is thrown.
                return stack.Close( monitor ) && success;
            }
            finally
            {
                // On error, don't save a dirty World's DefinitionFile.
                stack.Dispose();
            }
        }
    }

    /// <summary>
    /// The actual pull: this is "ckli pull" and the pull that "ckli push" always does before pushing.
    /// <para>
    /// Repositories are independent: they are pulled in parallel (bounded by <paramref name="maxDop"/> through
    /// the <see cref="ActivityMonitorAsyncPool"/> that also gives each of them its own monitor).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="continueOnError">
    /// True to pull every repository regardless of the errors. By default, the first error prevents any new
    /// repository from being pulled.
    /// </param>
    /// <param name="repos">The repositories to pull.</param>
    /// <param name="withTags">True to let the remote tags replace the local ones.</param>
    /// <param name="maxDop">Maximal degree of parallelism. 0 (or less) is unbounded.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    internal static async Task<bool> DoPullAsync( IActivityMonitor monitor,
                                                  bool continueOnError,
                                                  IReadOnlyList<Repo> repos,
                                                  bool withTags,
                                                  int maxDop,
                                                  CancellationToken cancellation )
    {
        using( monitor.OpenInfo( $"Pulling {repos.Count} repositories, {(withTags ? "updating" : "preserving")} local tags ({(maxDop <= 0 ? "parallel" : $"--max-dop {maxDop}")})." ) )
        {
            var pool = new ActivityMonitorAsyncPool( maxDop <= 0 ? int.MaxValue : maxDop );
            // SoftStop: on error, the started repositories end but no new one is considered.
            return await pool.ParallelAsync( repos,
                                             ( monitor, repo, cancellation ) => PullOne( monitor, repo, withTags, continueOnError, cancellation ),
                                             continueOnError ? ParallelErrorBehavior.Ignore : ParallelErrorBehavior.SoftStop,
                                             cancellation )
                             .ConfigureAwait( false );
        }
    }

    // Should this be the GitRepository.Pull method?
    static bool PullOne( IActivityMonitor monitor, Repo repo, bool withTags, bool continueOnError, CancellationToken cancellation )
    {
        // First, we Pull (Fetch + Merge on success).
        // We first fetch all the remote branches at once and then use MergeTrackedBranches to merge them (MergeTrackedBranches
        // correctly handles the branch that may be checked out).
        // 
        // Then we handle tags: by default only purely remote tags are fetched (safe fetch).
        // withTags = true may set TagFetchMode.Auto (tags that are referenced by the fetched objects will be retrieved)
        // but we want all tags to be updated. So we use the "tag pull *" below.
        //
        bool success = repo.GitRepository.FetchRemoteBranches( monitor, withTags: false, cancellation: cancellation )
                        && repo.GitRepository.MergeRemoteBranches( monitor, continueOnError, fromAllRemotes: false );
        if( success || continueOnError )
        {
            if( !withTags )
            {
                // "ckli pull" => "ckli tag fetch" => By default the tags are "safely fetched".
                success &= repo.GitRepository.FetchTags( monitor, cancellation: cancellation );
            }
            else
            {
                // "ckli pull --with-tags" => "ckli tag pull *" => git pull --tags --force
                success &= repo.GitRepository.PullTags( monitor, ["*"], cancellation: cancellation );
            }
        }
        return success;
    }
}
