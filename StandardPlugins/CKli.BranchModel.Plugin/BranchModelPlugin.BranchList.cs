using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    /// <summary>
    /// Implements "ckli branch list" (the command is handled by the HotZone plugin, that provides the
    /// <paramref name="versionResolver"/>): displays the opened branches of the World and, for each of them, what
    /// "ckli branch sync" would do and the repositories where it has changes.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="versionResolver">
    /// Optional provider of the resolver of the package versions that conflict: with it, a merge that conflicts on
    /// package versions only is a merge, as it is for "ckli branch sync". It receives the branch and a monitor that is
    /// not bound to the screen.
    /// </param>
    /// <returns>True on success, false if a solution cannot be read.</returns>
    public bool DisplayBranchList( IActivityMonitor monitor,
                                   CKliEnv context,
                                   Func<IActivityMonitor, BranchName, IPackageVersionResolver?>? versionResolver )
    {
        var repos = World.GetAllDefinedRepo( monitor );
        if( repos == null ) return false;
        var infos = repos.Select( r => Get( monitor, r ) ).ToArray();
        var reader = new SolutionReader( _shallowSolution );

        // The branch model is World global: this displays the BranchNamespace, not the Git branches of the
        // repositories (that is what "ckli issue" reports).
        var screen = context.Screen;
        var s = screen.ScreenType;
        // The link commits come from the ITagCommitProvider: a repository whose version tags have issues logs
        // errors there. Its sync status is simply unknown and "ckli issue" is what reports them, so they go to
        // a monitor that is not bound to the screen (they still reach the log file).
        var syncMonitor = new ActivityMonitor( "Computing the synchronization status of the branches." );
        // One row per branch: a multi line TextBlock trims each of its lines, so the indentation of the
        // tree is a Box margin, never spaces in the text.
        var cells = new List<(IRenderable Label, IRenderable? Sync, IRenderable Summary)>();
        try
        {
            foreach( var (b, depth) in _namespace.GetDisplayBranches() )
            {
                var label = b.IsRoot
                            ? s.Text( b.Name )!
                            : s.Text( $"{b.LinkType.ToCodeString()} {b.Name}" )!;
                string summary = b.IsRoot
                                    ? Repositories( repos.Count )
                                    : GetChangeSummary( monitor, repos, infos, b, reader );
                cells.Add( (label.Box( marginLeft: 2 * depth, marginRight: 2 ),
                            GetSyncStatus( syncMonitor, s, infos, b, versionResolver ),
                            s.Text( summary, ConsoleColor.DarkGray )) );
            }
        }
        finally
        {
            syncMonitor.MonitorEnd();
        }
        // A zero width cell is skipped by a HorizontalContent, which would shift the summary of its row into the
        // sync column: when the column exists, an empty cell is its margin. It exists only when needed.
        bool hasSyncColumn = cells.Any( c => c.Sync != null );
        var rows = cells.Select( c => hasSyncColumn
                                        ? c.Label.AddRight( c.Sync ?? s.EmptyString.Box( marginRight: 2 ), c.Summary )
                                        : c.Label.AddRight( c.Summary ) );
        // The ColumnDefinition headers are not implemented by the TableLayout: the header is its first row.
        IRenderable header = s.Text( "Branch", TextEffect.Underline ).Box( marginRight: 2 );
        header = hasSyncColumn
                    ? header.AddRight( s.Text( "Branch sync", TextEffect.Underline ).Box( marginRight: 2 ),
                                       s.Text( "Repositories", TextEffect.Underline ) )
                    : header.AddRight( s.Text( "Repositories", TextEffect.Underline ) );
        screen.Display( s.Text( $"Opened branches of '{World.Name}':" )!
                         .AddBelow( s.Unit.AddBelow( rows.Prepend( header ) ).TableLayout() ) );
        // The codes are compact on purpose (they align in a column and read as a propagation gradient) but
        // they are display only: this legend spells the names that the configuration and "--link" take.
        screen.Display( s.Text( "" )! );
        screen.Display( s.Text( "Links:" )! );
        foreach( var link in new[] { BranchLinkType.Manual, BranchLinkType.Release, BranchLinkType.CI, BranchLinkType.Full } )
        {
            screen.Display( s.Text( $"{link.ToCodeString()} {link}{LinkDescription( link )}" )!.Box( marginLeft: 2 ) );
        }
        return reader.Success;

        static string LinkDescription( BranchLinkType link ) => link switch
        {
            BranchLinkType.Manual => ": nothing is propagated from the parent.",
            BranchLinkType.Release => ": a version built on the parent is merged.",
            BranchLinkType.CI => " (the default): any commit built on the parent is merged.",
            _ => """: every commit of the parent's "dev/" branch is merged."""
        };
    }

    static string Repositories( int count ) => count == 1 ? "1 repository" : $"{count} repositories";

    /// <summary>
    /// What "ckli branch sync" would do to the branch from its link, for each repository where the branch exists.
    /// </summary>
    enum SyncStatus
    {
        UpToDate,
        FastForward,
        Merge,
        Conflict,
        Unknown
    }

    /// <summary>
    /// Summarizes what "ckli branch sync" would do to the (non root) branch <paramref name="b"/> across the repositories:
    /// the number of fast-forwards and merges, and the repositories where the merge conflicts or where the commit to
    /// integrate cannot be found. This is empty when the branch is up to date everywhere, when its link propagates
    /// nothing (<see cref="BranchLinkType.Manual"/>) and for the root branch: null is returned.
    /// <para>
    /// Only the link to the parent is considered: the merges of the remote branches that a synchronization starts
    /// with depend on a fetch, that is the business of "ckli pull".
    /// </para>
    /// </summary>
    IRenderable? GetSyncStatus( IActivityMonitor monitor,
                                ScreenType s,
                                BranchModelInfo[] infos,
                                BranchName b,
                                Func<IActivityMonitor, BranchName, IPackageVersionResolver?>? versionResolver )
    {
        if( b.IsRoot || b.LinkType is BranchLinkType.Manual or BranchLinkType.None )
        {
            return null;
        }
        int fastForwards = 0;
        int merges = 0;
        var conflicts = new List<string>();
        var unknowns = new List<string>();
        foreach( var info in infos )
        {
            switch( GetSyncStatus( monitor, info, b, versionResolver ) )
            {
                case SyncStatus.FastForward: ++fastForwards; break;
                case SyncStatus.Merge: ++merges; break;
                case SyncStatus.Conflict: conflicts.Add( info.Repo.DisplayPath.Path ); break;
                case SyncStatus.Unknown: unknowns.Add( info.Repo.DisplayPath.Path ); break;
            }
        }
        var parts = new List<IRenderable>();
        if( fastForwards > 0 ) parts.Add( s.Text( fastForwards == 1 ? "1 fast-forward" : $"{fastForwards} fast-forwards", ConsoleColor.DarkGray ) );
        if( merges > 0 ) parts.Add( s.Text( merges == 1 ? "1 merge" : $"{merges} merges", ConsoleColor.DarkGray ) );
        if( conflicts.Count > 0 ) parts.Add( s.Text( $"{(conflicts.Count == 1 ? "1 conflict" : $"{conflicts.Count} conflicts")} ({conflicts.Concatenate( ", " )})", ConsoleColor.Red ) );
        if( unknowns.Count > 0 ) parts.Add( s.Text( $"{unknowns.Count} unknown ({unknowns.Concatenate( ", " )})", ConsoleColor.Yellow ) );
        if( parts.Count == 0 )
        {
            return null;
        }
        // A TextBlock trims its content: the separators' spaces are margins.
        IRenderable cell = parts[0];
        for( int i = 1; i < parts.Count; ++i )
        {
            cell = cell.AddRight( s.Text( ",", ConsoleColor.DarkGray ), parts[i].Box( marginLeft: 1 ) );
        }
        return cell.Box( marginRight: 2 );
    }

    /// <summary>
    /// Classifies the branch <paramref name="b"/> of a repository the same way <see cref="HotBranch.Synchronize"/>
    /// integrates its <see cref="HotBranch.GetLinkCommit"/>: nothing to do when the commit is already reachable or
    /// brings no content, a fast-forward, or a merge that is computed (in the object database only) to detect a conflict.
    /// With a <paramref name="versionResolver"/>, a merge that conflicts on package versions only is a merge.
    /// </summary>
    SyncStatus? GetSyncStatus( IActivityMonitor monitor,
                               BranchModelInfo info,
                               BranchName b,
                               Func<IActivityMonitor, BranchName, IPackageVersionResolver?>? versionResolver )
    {
        Throw.DebugAssert( b.Parent != null );
        var hb = info.Branches[b.Index];
        if( !hb.Exists ) return null;
        var parent = info.GetClosestExistingBranch( b.Parent );
        if( parent == null ) return null;
        // Without a provider, Release and CI links cannot be honored (Synchronize would throw).
        if( b.LinkType is not BranchLinkType.Full && TagCommitProvider == null ) return SyncStatus.Unknown;
        var linkCommit = hb.GetLinkCommit( monitor, parent, b.LinkType );
        if( linkCommit == null ) return SyncStatus.Unknown;

        var git = info.Repo.GitRepository.Repository;
        var tip = (hb.GitDevBranch ?? hb.GitBranch).Tip;
        var d = git.ObjectDatabase.CalculateHistoryDivergence( tip, linkCommit );
        if( d.BehindBy is 0 ) return SyncStatus.UpToDate;
        if( d.AheadBy is 0 ) return SyncStatus.FastForward;
        // Diverged with the same content: Synchronize creates no empty merge commit.
        if( tip.Tree.Sha == linkCommit.Tree.Sha ) return SyncStatus.UpToDate;
        var merge = git.ObjectDatabase.MergeCommits( tip, linkCommit, new MergeTreeOptions { SkipReuc = true, FailOnConflict = true } );
        if( merge.Tree != null ) return SyncStatus.Merge;
        if( versionResolver == null ) return SyncStatus.Conflict;
        // The same aligned merge as the synchronization (its objects are left unreferenced in the object database).
        return PackageVersionMerge.CreateMergeCommit( monitor,
                                                      info.Repo.GitRepository,
                                                      tip,
                                                      linkCommit,
                                                      b.Name,
                                                      $"commit '{linkCommit.Sha.AsSpan( 0, 7 )} {linkCommit.MessageShort}'",
                                                      () => versionResolver( monitor, b ),
                                                      out _ ) != null
                ? SyncStatus.Merge
                : SyncStatus.Conflict;
    }

    /// <summary>
    /// Summarizes the repositories where the (non root) branch <paramref name="b"/> has changes: its tips, the
    /// count of the repositories where it is opened without changes and its weight (the repositories and projects
    /// that a build touches: the ones with changes and all their downstreams, unchanged ones included).
    /// <para>
    /// Upstreams are the producers of the consumed packages, read from each repository's closest existing branch:
    /// a repository where <paramref name="b"/> doesn't exist still links its upstreams to its downstreams.
    /// </para>
    /// </summary>
    static string GetChangeSummary( IActivityMonitor monitor,
                                     IReadOnlyList<Repo> repos,
                                     BranchModelInfo[] infos,
                                     BranchName b,
                                     SolutionReader reader )
    {
        Throw.DebugAssert( !b.IsRoot );
        int count = repos.Count;
        var withChanges = new bool[count];
        int unchangedCount = 0;
        var solutions = new GitSolution?[count];
        for( int i = 0; i < count; i++ )
        {
            var info = infos[i];
            var hb = info.Branches[b.Index];
            if( hb.Exists )
            {
                if( IsUnchanged( info, hb ) ) ++unchangedCount;
                else withChanges[i] = true;
            }
            var closest = info.GetClosestExistingBranch( b );
            if( closest != null )
            {
                solutions[i] = reader.Read( monitor, repos[i], closest.GitDevBranch ?? closest.GitBranch! );
            }
        }
        if( !withChanges.Contains( true ) )
        {
            return unchangedCount == 0 ? "No change." : $"No change, {unchangedCount} unchanged.";
        }
        var producers = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
        for( int i = 0; i < count; i++ )
        {
            var sol = solutions[i];
            if( sol != null ) RegisterProducer( producers, i, sol );
        }
        var upstreams = new List<int>[count];
        var downstreams = new List<int>[count];
        for( int i = 0; i < count; i++ )
        {
            upstreams[i] = new List<int>();
            downstreams[i] = new List<int>();
        }
        for( int i = 0; i < count; i++ )
        {
            var sol = solutions[i];
            if( sol == null ) continue;
            foreach( var c in sol.Consumed )
            {
                if( producers.TryGetValue( c.PackageId, out var u ) && u != i && !upstreams[i].Contains( u ) )
                {
                    upstreams[i].Add( u );
                    downstreams[u].Add( i );
                }
            }
        }
        var tips = new List<string>();
        int others = 0;
        for( int i = 0; i < count; i++ )
        {
            if( !withChanges[i] ) continue;
            // The upstreams are transitive: a repository without changes in between still links them.
            if( Reach( [i], upstreams, includeStart: false ).Any( u => withChanges[u] ) ) ++others;
            else tips.Add( repos[i].DisplayPath.Path );
        }
        // The weight is what a build of the branch touches: the repositories with changes and all their
        // downstreams (an unchanged one is updated with its upstream's new version).
        var weight = Reach( withChanges.Select( ( c, i ) => c ? i : -1 ).Where( i => i >= 0 ), downstreams, includeStart: true );
        int projectCount = weight.Sum( i => solutions[i]?.Projects.Count ?? 0 );

        var summary = tips.Concatenate( ", " );
        if( others > 0 ) summary += $" and {others} other {(others == 1 ? "repository" : "repositories")}";
        if( unchangedCount > 0 ) summary += $", {unchangedCount} unchanged";
        return summary + $", weight: {Repositories( weight.Count )}, {(projectCount == 1 ? "1 project" : $"{projectCount} projects")}.";

        static HashSet<int> Reach( IEnumerable<int> starts, List<int>[] edges, bool includeStart )
        {
            var reached = new HashSet<int>();
            var toProcess = new Stack<int>();
            foreach( var s in starts )
            {
                if( includeStart ) reached.Add( s );
                toProcess.Push( s );
            }
            while( toProcess.TryPop( out var n ) )
            {
                foreach( var next in edges[n] )
                {
                    if( reached.Add( next ) ) toProcess.Push( next );
                }
            }
            return reached;
        }
    }

    /// <summary>
    /// A branch is unchanged when it brings no change of its own: the tree of its tip ("dev/" first) is the tree of
    /// its merge base with its closest existing parent. This is about content, not commits (on purpose, unlike the
    /// <see cref="BranchLink.IssueKind.Useless"/> "dev/" branch): the empty merge commits of a synchronization and the
    /// "Producing 'vX' from unchanged head." commits bring nothing to a reader of the system.
    /// </summary>
    static bool IsUnchanged( BranchModelInfo info, HotBranch hb )
    {
        Throw.DebugAssert( hb.Exists && hb.BranchName.Parent != null );
        var parent = info.GetClosestExistingBranch( hb.BranchName.Parent );
        if( parent == null ) return false;
        var tip = (hb.GitDevBranch ?? hb.GitBranch).Tip;
        var parentTip = (parent.GitDevBranch ?? parent.GitBranch!).Tip;
        var mergeBase = info.Repo.GitRepository.Repository.ObjectDatabase.FindMergeBase( tip, parentTip );
        // Unrelated histories have changes of their own.
        return mergeBase != null && mergeBase.Tree.Sha == tip.Tree.Sha;
    }

    /// <summary>
    /// The same project name to package identifier heuristic as the HotGraph: an explicitly
    /// non packable project produces nothing and an explicitly packable one wins.
    /// </summary>
    static void RegisterProducer( Dictionary<string, int> producers, int index, GitSolution solution )
    {
        foreach( var p in solution.Projects )
        {
            if( p.IsPackable is false ) continue;
            if( !producers.TryAdd( p.Name, index ) && p.IsPackable is true )
            {
                producers[p.Name] = index;
            }
        }
    }

    /// <summary>
    /// Reads the solutions once per commit: the closest existing branch of a repository is often
    /// the same for several branches.
    /// </summary>
    sealed class SolutionReader( ShallowSolutionPlugin shallowSolution )
    {
        readonly Dictionary<(Repo, ObjectId), GitSolution?> _cache = new();

        public bool Success { get; private set; } = true;

        public GitSolution? Read( IActivityMonitor monitor, Repo repo, Branch branch )
        {
            var key = (repo, branch.Tip.Id);
            if( !_cache.TryGetValue( key, out var solution ) )
            {
                // A missing ".slnx" is not an error: such a repository produces and consumes nothing.
                if( !shallowSolution.TryGetShallowSolution( monitor, repo, branch, useWorkingFolder: false, out solution ) )
                {
                    Success = false;
                }
                _cache.Add( key, solution );
            }
            return solution;
        }
    }
}
