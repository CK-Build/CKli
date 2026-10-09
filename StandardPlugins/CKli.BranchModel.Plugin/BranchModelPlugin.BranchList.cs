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
    /// <paramref name="versionResolver"/>): displays the opened branches of the World, the repositories where each of
    /// them has changes and, on the edge between a branch and its parent, what "ckli branch close" would merge into
    /// the parent (↖) and what "ckli branch sync" would merge into the branch (↘).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="link">
    /// Optional link type (Regular, CI or Full) that the synchronization predictions consider instead of each
    /// branch's configured one, like the "--link" of "ckli branch sync".
    /// </param>
    /// <param name="versionResolver">
    /// Optional provider of the resolver of the package versions that conflict: with it, a merge that conflicts on
    /// package versions only is a merge, as it is for "ckli branch sync" and "ckli branch close". It receives the
    /// branch that the merge goes into and is called at most once per branch.
    /// </param>
    /// <returns>True on success, false if the link is invalid, a root branch is missing or a solution cannot be read.</returns>
    public bool DisplayBranchList( IActivityMonitor monitor,
                                   CKliEnv context,
                                   string? link,
                                   Func<IActivityMonitor, BranchName, IPackageVersionResolver?>? versionResolver )
    {
        if( !ParseLink( monitor, link, allowManual: false, out var applyLink ) )
        {
            return false;
        }
        var repos = World.GetAllDefinedRepo( monitor );
        if( repos == null ) return false;
        var infos = GetInfos( monitor, repos );
        if( infos == null ) return false;
        var reader = new SolutionReader( _shallowSolution );
        var resolvers = new ResolverCache( versionResolver );

        // The branch model is World global: this displays the BranchNamespace, not the Git branches of the
        // repositories (that is what "ckli issue" reports).
        var screen = context.Screen;
        var s = screen.ScreenType;
        // A branch row is split in 2 columns (the branch and its repositories). The merge rows are single cells
        // that span the whole line: they don't widen the branch column. A multi line TextBlock trims each of its
        // lines, so the indentation of the tree is a Box margin, never spaces in the text.
        var rows = new List<IRenderable>();
        bool hasMerges = false;
        foreach( var (b, depth) in _namespace.GetDisplayBranches() )
        {
            if( !b.IsRoot )
            {
                // The edge between the branch and its parent, right above the branch: the close goes up into the
                // parent, the synchronization comes down into the branch. They are aligned on the branch's name.
                var close = GetMergeLine( s, "↖", infos, info => PredictClose( monitor, info, b, resolvers ) );
                var sync = GetMergeLine( s, "↘", infos, info => PredictSynchronize( monitor, info, b, applyLink, resolvers ) );
                if( close != null ) rows.Add( close.Box( marginLeft: 2 * depth + 3 ) );
                if( sync != null ) rows.Add( sync.Box( marginLeft: 2 * depth + 3 ) );
                hasMerges |= close != null || sync != null;
            }
            var label = b.IsRoot
                        ? s.Text( b.Name )!
                        : s.Text( $"{b.LinkType.ToCodeString()} {b.Name}" )!;
            string summary = b.IsRoot
                                ? Repositories( repos.Count )
                                : GetChangeSummary( monitor, repos, infos, b, reader );
            rows.Add( label.Box( marginLeft: 2 * depth, marginRight: 2 ).AddRight( s.Text( summary, ConsoleColor.DarkGray ) ) );
        }
        // The ColumnDefinition headers are not implemented by the TableLayout: the header is its first row.
        var header = s.Text( "Branch", TextEffect.Underline ).Box( marginRight: 2 )
                        .AddRight( s.Text( "Repositories", TextEffect.Underline ) );
        screen.Display( s.Text( $"Opened branches of '{World.Name}':" )!
                         .AddBelow( s.Unit.AddBelow( rows.Prepend( header ) ).TableLayout() ) );
        // The codes are compact on purpose (they align in a column and read as a propagation gradient) but
        // they are display only: this legend spells the names that the configuration and "--link" take.
        screen.Display( s.Text( "" )! );
        screen.Display( s.Text( "Links:" )! );
        foreach( var l in new[] { BranchLinkType.Manual, BranchLinkType.Regular, BranchLinkType.CI, BranchLinkType.Full } )
        {
            screen.Display( s.Text( $"{l.ToCodeString()} {l}{LinkDescription( l )}" )!.Box( marginLeft: 2 ) );
        }
        if( hasMerges )
        {
            string syncCommand = applyLink is BranchLinkType.None ? "ckli branch sync" : $"ckli branch sync --link {applyLink}";
            screen.Display( s.Text( "" )! );
            screen.Display( s.Text( "Merges:" )! );
            screen.Display( s.Text( """↖ "ckli branch close": what closing the branch would merge into its parent.""" )!.Box( marginLeft: 2 ) );
            screen.Display( s.Text( $"""↘ "{syncCommand}": what synchronizing the branch would merge into it.""" )!.Box( marginLeft: 2 ) );
        }
        return reader.Success;

        static string LinkDescription( BranchLinkType link ) => link switch
        {
            BranchLinkType.Manual => ": nothing is synchronized from the parent.",
            BranchLinkType.Regular => ": the parent's last regular version is merged.",
            BranchLinkType.CI => ": the parent's last version, regular or CI, is merged.",
            _ => """ (the default): the parent's "dev/" branch is merged."""
        };
    }

    static string Repositories( int count ) => count == 1 ? "1 repository" : $"{count} repositories";

    /// <summary>
    /// Summarizes the outcomes of a merge across the repositories, prefixed by its <paramref name="arrow"/>: the number
    /// of fast-forwards and merges, and the repositories where the merge conflicts or cannot be computed (the commit to
    /// integrate cannot be found, for instance). Null when there is nothing to merge anywhere.
    /// </summary>
    static IRenderable? GetMergeLine( ScreenType s, string arrow, BranchModelInfo[] infos, Func<BranchModelInfo, MergeOutcome?> predict )
    {
        int fastForwards = 0;
        int merges = 0;
        var conflicts = new List<Repo>();
        var unknowns = new List<Repo>();
        foreach( var info in infos )
        {
            switch( predict( info ) )
            {
                case MergeOutcome.FastForward: ++fastForwards; break;
                case MergeOutcome.Merge: ++merges; break;
                case MergeOutcome.Conflict: conflicts.Add( info.Repo ); break;
                case MergeOutcome.Failed: unknowns.Add( info.Repo ); break;
            }
        }
        var parts = new List<IRenderable>();
        if( fastForwards > 0 ) parts.Add( s.Text( fastForwards == 1 ? "1 fast-forward" : $"{fastForwards} fast-forwards", ConsoleColor.DarkGray ) );
        if( merges > 0 ) parts.Add( s.Text( merges == 1 ? "1 merge" : $"{merges} merges", ConsoleColor.DarkGray ) );
        if( conflicts.Count > 0 ) parts.Add( RepoList( s, conflicts.Count == 1 ? "1 conflict" : $"{conflicts.Count} conflicts", conflicts, ConsoleColor.Red ) );
        if( unknowns.Count > 0 ) parts.Add( RepoList( s, $"{unknowns.Count} unknown", unknowns, ConsoleColor.Yellow ) );
        if( parts.Count == 0 )
        {
            return null;
        }
        // A TextBlock trims its content: the separators' spaces are margins.
        IRenderable line = s.Text( arrow, ConsoleColor.DarkGray ).AddRight( parts[0].Box( marginLeft: 1 ) );
        for( int i = 1; i < parts.Count; ++i )
        {
            line = line.AddRight( s.Text( ",", ConsoleColor.DarkGray ), parts[i].Box( marginLeft: 1 ) );
        }
        return line;

        // "<label> (<repo>, <repo>)": the repositories are linked and show their dirty marker, since a dirty
        // working folder may be the cause of the outcome.
        static IRenderable RepoList( ScreenType s, string label, List<Repo> repos, ConsoleColor color )
        {
            var style = new TextStyle( color );
            IRenderable list = s.Text( label + " (", color ).AddRight( repos[0].ToInlineNameRenderable( s, style ) );
            for( int i = 1; i < repos.Count; ++i )
            {
                list = list.AddRight( s.Text( ",", color ), repos[i].ToInlineNameRenderable( s, style ).Box( marginLeft: 1 ) );
            }
            return list.AddRight( s.Text( ")", color ) );
        }
    }

    /// <summary>
    /// What "ckli branch close" would merge into the closest existing parent of the (non root) branch <paramref name="b"/>
    /// in a repository: null when the branch or its parent doesn't exist there.
    /// </summary>
    static MergeOutcome? PredictClose( IActivityMonitor monitor, BranchModelInfo info, BranchName b, ResolverCache resolvers )
    {
        Throw.DebugAssert( b.Parent != null );
        var hb = info.Branches[b.Index];
        if( !hb.Exists ) return null;
        var parent = info.GetClosestExistingBranch( b.Parent );
        if( parent == null ) return null;
        return hb.PredictClose( monitor, resolvers.For( parent.BranchName ), out _ );
    }

    /// <summary>
    /// What "ckli branch sync" would merge into the (non root) branch <paramref name="b"/> in a repository from its
    /// link (or the <paramref name="applyLink"/> override): null when the branch or its parent doesn't exist there,
    /// and when the link propagates nothing (<see cref="BranchLinkType.Manual"/>).
    /// <para>
    /// Only the link to the parent is considered: the merges of the remote branches that a synchronization starts
    /// with depend on a fetch, that is the business of "ckli pull".
    /// </para>
    /// </summary>
    MergeOutcome? PredictSynchronize( IActivityMonitor monitor, BranchModelInfo info, BranchName b, BranchLinkType applyLink, ResolverCache resolvers )
    {
        Throw.DebugAssert( b.Parent != null );
        var linkType = applyLink is BranchLinkType.None ? b.LinkType : applyLink;
        if( linkType is BranchLinkType.Manual or BranchLinkType.None ) return null;
        var hb = info.Branches[b.Index];
        if( !hb.Exists ) return null;
        if( info.GetClosestExistingBranch( b.Parent ) == null ) return null;
        // Without a provider, Regular and CI links cannot be honored (the link commit cannot be found).
        if( linkType is not BranchLinkType.Full && TagCommitProvider == null ) return MergeOutcome.Failed;
        return hb.PredictSynchronize( monitor, linkType, resolvers.For( b ), out _ );
    }

    /// <summary>
    /// Requests the resolver of the package versions that conflict once per branch that a merge goes into, when a
    /// merge first conflicts. When it is not available (the provider has logged why), the conflicts stay conflicts
    /// and this is said once.
    /// </summary>
    sealed class ResolverCache( Func<IActivityMonitor, BranchName, IPackageVersionResolver?>? provider )
    {
        readonly Dictionary<BranchName, IPackageVersionResolver?> _resolvers = new();

        public Func<IActivityMonitor, IPackageVersionResolver?>? For( BranchName b )
        {
            if( provider == null ) return null;
            return monitor =>
            {
                if( !_resolvers.TryGetValue( b, out var resolver ) )
                {
                    resolver = provider( monitor, b );
                    if( resolver == null )
                    {
                        monitor.Warn( $"The package versions that conflict when merging into '{b}' cannot be resolved (see above): they are counted as conflicts." );
                    }
                    _resolvers.Add( b, resolver );
                }
                return resolver;
            };
        }
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
