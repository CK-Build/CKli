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
    /// Displays the opened branches of the World and, for each of them, the repositories where it has changes.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <returns>True on success, false if a solution cannot be read.</returns>
    [Description( """
        Displays the opened branches of the World and how each one is linked to its parent.
        For each branch, the repositories where it has changes are summarized by their tips (the ones that have no
        upstream repository where the branch has changes), the number of repositories where the branch is opened
        without changes and the weight of the branch: the number of repositories and projects that a build of the
        branch touches (the ones with changes and all their downstreams).
        """ )]
    [CommandPath( "branch list" )]
    public bool BranchList( IActivityMonitor monitor, CKliEnv context )
    {
        var repos = World.GetAllDefinedRepo( monitor );
        if( repos == null ) return false;
        var infos = repos.Select( r => Get( monitor, r ) ).ToArray();
        var reader = new SolutionReader( _shallowSolution );

        // The branch model is World global: this displays the BranchNamespace, not the Git branches of the
        // repositories (that is what "ckli issue" reports).
        var screen = context.Screen;
        var s = screen.ScreenType;
        // One row per branch: a multi line TextBlock trims each of its lines, so the indentation of the
        // tree is a Box margin, never spaces in the text.
        var rows = new List<IRenderable>();
        foreach( var (b, depth) in _namespace.GetDisplayBranches() )
        {
            var label = b.IsRoot
                        ? s.Text( b.Name )!
                        : s.Text( $"{b.LinkType.ToCodeString()} {b.Name}" )!;
            string summary = b.IsRoot
                                ? Repositories( repos.Count )
                                : GetChangeSummary( monitor, repos, infos, b, reader );
            rows.Add( label.Box( marginLeft: 2 * depth, marginRight: 2 )
                           .AddRight( s.Text( summary, ConsoleColor.DarkGray ) ) );
        }
        screen.Display( s.Text( $"Opened branches of '{World.Name}':" )!
                         .AddBelow( s.Unit.AddBelow( rows ).TableLayout() ) );
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
