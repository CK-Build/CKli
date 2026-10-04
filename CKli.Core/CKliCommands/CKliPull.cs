using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
                A merge that conflicts beyond what the plugins align (the package versions) is not merged, unless it is on the branch
                named by --branch: that branch is checked out and the merge is left in progress in its working folder.
                """,
                [],
                [
                    (["--branch", "-b"], "The branch you work on: a merge that conflicts there is left in progress (its \"dev/\" branch when it exists).", false),
                ],
                [
                    (["--all"], "Consider all the Repos' of the current World (even if current path is in a Repo)."),
                    (["--with-tags"], "Pull tags: remote tags replace local ones with the same name."),
                    (["--continue-on-error"], "Continues even on error. By default the first error stops the operation."),
                    (["--dry-run", "-d"], "Fetches and displays what the merges would do: nothing is merged."),
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
        string? branch = cmdLine.EatSingleOption( "--branch", "-b" );
        bool all = cmdLine.EatFlag( "--all" );
        bool withTags = cmdLine.EatFlag( "--with-tags" );
        bool continueOnError = cmdLine.EatFlag( "--continue-on-error" );
        bool dryRun = cmdLine.EatFlag( "--dry-run", "-d" );
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
        return new ValueTask<bool>( PullAsync( monitor, this, context, all, maxDop, withTags, continueOnError, branch, dryRun, scopeAlive ) );


        static async Task<bool> PullAsync( IActivityMonitor monitor,
                                           Command command,
                                           CKliEnv context,
                                           bool all,
                                           int maxDop,
                                           bool withTags,
                                           bool continueOnError,
                                           string? branch,
                                           bool dryRun,
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

                bool success = await DoPullAsync( monitor, world, context.Screen, repos, withTags, maxDop, continueOnError, branch, dryRun, scopeAlive )
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
    /// Repositories are independent: they are fetched and their merges that don't conflict are done in parallel
    /// (bounded by <paramref name="maxDop"/> through the <see cref="ActivityMonitorAsyncPool"/> that also gives each of
    /// them its own monitor). The merges that conflict are handled last, one after the other, once the World is settled:
    /// the <see cref="World.MergeAssistant"/> may align their sides (see <see cref="MergeSidesAligner"/>). A merge that
    /// still conflicts is left in progress on the <paramref name="branch"/> and not merged on any other branch: both
    /// make the pull fail and are displayed.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="world">The World.</param>
    /// <param name="screen">The screen that displays the merges that conflict.</param>
    /// <param name="repos">The repositories to pull.</param>
    /// <param name="withTags">True to let the remote tags replace the local ones.</param>
    /// <param name="maxDop">Maximal degree of parallelism. 0 (or less) is unbounded.</param>
    /// <param name="continueOnError">
    /// True to fetch and merge every repository regardless of the errors. By default, the first error prevents any new
    /// repository from being pulled. The merges that conflict are always all handled.
    /// </param>
    /// <param name="branch">The branch that the person works on: a merge that conflicts there is left in progress.</param>
    /// <param name="dryRun">True to only fetch and display what the merges would do.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>True on success, false on error or when a merge conflicts.</returns>
    internal static async Task<bool> DoPullAsync( IActivityMonitor monitor,
                                                  World world,
                                                  IScreen screen,
                                                  IReadOnlyList<Repo> repos,
                                                  bool withTags,
                                                  int maxDop,
                                                  bool continueOnError,
                                                  string? branch,
                                                  bool dryRun,
                                                  CancellationToken cancellation )
    {
        var conflicting = new ConcurrentQueue<(Repo Repo, Branch Branch)>();
        var outcomes = dryRun ? new ConcurrentQueue<MergeOutcome>() : null;
        bool success;
        using( monitor.OpenInfo( $"Pulling {repos.Count} repositories, {(withTags ? "updating" : "preserving")} local tags ({(maxDop <= 0 ? "parallel" : $"--max-dop {maxDop}")})." ) )
        {
            var pool = new ActivityMonitorAsyncPool( maxDop <= 0 ? int.MaxValue : maxDop );
            // SoftStop: on error, the started repositories end but no new one is considered.
            success = await pool.ParallelAsync( repos,
                                                ( monitor, repo, cancellation ) => PullOne( monitor, repo, withTags, continueOnError, conflicting, outcomes, cancellation ),
                                                continueOnError ? ParallelErrorBehavior.Ignore : ParallelErrorBehavior.SoftStop,
                                                cancellation )
                                .ConfigureAwait( false );
        }
        var prepared = new List<PreparedMerge>();
        var notMerged = new List<PreparedMerge>();
        if( !conflicting.IsEmpty )
        {
            using( monitor.OpenInfo( $"Handling {conflicting.Count} merge(s) that conflict." ) )
            using( var session = world.MergeAssistant?.OpenSession( monitor ) )
            {
                foreach( var (repo, b) in conflicting.OrderBy( c => c.Repo.DisplayPath.Path ).ThenBy( c => c.Branch.FriendlyName ) )
                {
                    success &= HandleConflict( monitor, session, repo, b, branch, outcomes, prepared, notMerged );
                }
            }
        }
        if( outcomes != null )
        {
            PreparedMerge.DisplayDryRun( screen, outcomes.ToList(), prepared, notMerged );
            return success && prepared.Count == 0 && notMerged.Count == 0;
        }
        PreparedMerge.Display( screen, prepared );
        PreparedMerge.DisplayNotMerged( screen, notMerged );
        return success;
    }

    // Fetches the repository and merges its tracked branches that don't conflict: the ones that do are queued.
    static bool PullOne( IActivityMonitor monitor,
                         Repo repo,
                         bool withTags,
                         bool continueOnError,
                         ConcurrentQueue<(Repo Repo, Branch Branch)> conflicting,
                         ConcurrentQueue<MergeOutcome>? outcomes,
                         CancellationToken cancellation )
    {
        // First, we fetch all the remote branches at once, then each tracked branch is merged into its local one
        // (MergeTrackedBranch correctly handles the branch that may be checked out).
        //
        // Then we handle tags: by default only purely remote tags are fetched (safe fetch).
        // withTags = true may set TagFetchMode.Auto (tags that are referenced by the fetched objects will be retrieved)
        // but we want all tags to be updated. So we use the "tag pull *" below.
        //
        var git = repo.GitRepository;
        bool success = git.FetchRemoteBranches( monitor, withTags: false, cancellation: cancellation );
        if( success )
        {
            foreach( var b in git.GetTrackedBranches( monitor ) )
            {
                var trackedTip = b.TrackedBranch?.Tip;
                if( trackedTip == null ) continue;
                var outcome = git.PredictMerge( monitor, b.Tip, trackedTip, aligner: null, out _ );
                if( outcome == MergeOutcome.Conflict )
                {
                    conflicting.Enqueue( (repo, b) );
                }
                else if( outcomes != null )
                {
                    outcomes.Enqueue( outcome );
                }
                else
                {
                    var refB = b;
                    success &= git.MergeTrackedBranch( monitor, ref refB );
                    if( !success && !continueOnError ) return false;
                }
            }
        }
        // A dry run changes no tag.
        if( outcomes == null && (success || continueOnError) )
        {
            if( !withTags )
            {
                // "ckli pull" => "ckli tag fetch" => By default the tags are "safely fetched".
                success &= git.FetchTags( monitor, cancellation: cancellation );
            }
            else
            {
                // "ckli pull --with-tags" => "ckli tag pull *" => git pull --tags --force
                success &= git.PullTags( monitor, ["*"], cancellation: cancellation );
            }
        }
        return success;
    }

    // Merges a tracked branch that conflicts: its sides are aligned when the merge assistant can, and when a conflict
    // remains, the merge is left in progress if it is the working branch, otherwise it is not merged.
    static bool HandleConflict( IActivityMonitor monitor,
                                IMergeSession? session,
                                Repo repo,
                                Branch b,
                                string? branch,
                                ConcurrentQueue<MergeOutcome>? outcomes,
                                List<PreparedMerge> prepared,
                                List<PreparedMerge> notMerged )
    {
        var git = repo.GitRepository;
        b = git.Repository.Branches[b.CanonicalName];
        var tracked = b.TrackedBranch;
        var theirsName = $"branch '{tracked.FriendlyName}'";
        var aligner = session?.GetAligner( monitor, repo, b );
        var working = branch == null
                        ? null
                        : session != null
                            ? session.GetWorkingBranch( monitor, repo, branch )
                            : git.GetBranch( monitor, branch, CK.Core.LogLevel.None );
        bool isWorking = working != null && working.CanonicalName == b.CanonicalName;
        if( outcomes != null )
        {
            var outcome = git.PredictMerge( monitor, b.Tip, tracked.Tip, aligner, out var predicted );
            outcomes.Enqueue( outcome );
            if( outcome == MergeOutcome.Conflict )
            {
                (isWorking ? prepared : notMerged).Add( new PreparedMerge( repo, b.FriendlyName, theirsName, predicted ) );
            }
            return outcome != MergeOutcome.Failed;
        }
        var merge = git.CreateMergeCommit( monitor, b.Tip, tracked.Tip, theirsName, aligner, out var conflicts );
        if( merge != null )
        {
            // The conflicts have been aligned: the branch is fast-forwarded to the merge commit.
            return git.MergeBranchContent( monitor, ref b, merge );
        }
        // An error has been logged.
        if( conflicts.Count == 0 ) return false;
        monitor.Info( $"""
            Merging {theirsName} into '{b.FriendlyName}' in '{repo.DisplayPath}' conflicts in:
            {conflicts.Concatenate( System.Environment.NewLine )}
            """ );
        if( isWorking )
        {
            if( !b.IsCurrentRepositoryHead )
            {
                if( git.Checkout( monitor, b ) )
                {
                    monitor.Info( $"'{b.FriendlyName}' is checked out in '{repo.DisplayPath}' to resolve its conflicts." );
                    b = git.Repository.Branches[b.CanonicalName];
                }
            }
            if( b.IsCurrentRepositoryHead
                && git.PrepareMerge( monitor, b, tracked.Tip, theirsName, aligner, out var inProgress ) )
            {
                prepared.Add( new PreparedMerge( repo, b.FriendlyName, theirsName, inProgress ) );
                return false;
            }
        }
        notMerged.Add( new PreparedMerge( repo, b.FriendlyName, theirsName, conflicts ) );
        return false;
    }
}
