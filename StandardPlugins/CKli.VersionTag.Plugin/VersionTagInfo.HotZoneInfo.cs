using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;


namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagInfo
{
    /// <summary>
    /// Captures the "hot zone" version tags.
    /// </summary>
    public sealed class HotZoneInfo
    {
        readonly VersionTagInfo _info;
        readonly TagCommit _topHot;
        readonly TagCommit _lastStable;
        readonly World.Issue? _hotZoneIssue;
        Dictionary<string, TagCommitTree?>? _commitTrees;

        HotZoneInfo( VersionTagInfo info, TagCommit lastStable, TagCommit topHot, World.Issue? hotZoneIssue )
        {
            _info = info;
            _lastStable = lastStable;
            _topHot = topHot;
            _hotZoneIssue = hotZoneIssue;
        }

        internal static HotZoneInfo Create( IActivityMonitor monitor, VersionTagInfo info, TagCommit lastStable, TagCommit topHot )
        {
            Throw.DebugAssert( lastStable.Version.IsStable );
            string? message = null;
            if( topHot.Version != lastStable.Version )
            {
                if( lastStable.IsOrHasFakeVersion )
                {
                    Throw.DebugAssert( (lastStable.IsFakeVersion && !lastStable.IsBuildingOrLocal) || (lastStable.FakeVersion != null && lastStable.IsBuildingOrLocal) );
                    var fake = lastStable.FakeVersion?.Version ?? lastStable.Version; 
                    // A "+fake" is a STARTING version: it declares its own version to be produced here, so
                    // only that version's prereleases and CI builds may appear above it. Its successors
                    // cannot: they only become legitimate once the fake's version is published - and by
                    // then the fake is gone (removableTags) and this branch no longer applies.
                    if( !fake.SameStableAs( topHot.Version ) )
                    {
                        message = $"""
                              The greatest version tag '{topHot.Version.ParsedText}' is invalid because the current stable version is '{fake.ParsedText}'.
                              This should be fixed manually.
                              """;
                    }
                }
                else
                {
                    var hotSupremum = SVersion.Create( lastStable.Version.Major + 1, 0, 0 );
                    if( topHot.Version >= hotSupremum )
                    {
                        message = $"""
                              The greatest version tag '{topHot.Version.ParsedText}' cannot be greater or equal to 'v{hotSupremum.Major}.0.0' because the last published stable version is '{lastStable.Version.ParsedText}'.
                              This should be fixed manually.
                              """;
                    }
                }
            }
            World.Issue? hotZoneIssue = null;
            if( message != null )
            {
                monitor.Warn( $"Hot zone issue in '{info.Repo.DisplayPath}': {message}" );
                hotZoneIssue = World.Issue.CreateManual( "Hot zone issue detected.", info.Repo.World.ScreenType.Text( message ), info.Repo );
            }
            return new HotZoneInfo( info, lastStable, topHot, hotZoneIssue );
        }

        /// <summary>
        /// Gets the <see cref="VersionTagInfo"/> for this repository.
        /// </summary>
        public VersionTagInfo VersionTagInfo => _info;

        /// <summary>
        /// Gets a manual issue if <see cref="TopHot"/> is greater or equal to the next major of the last published stable version.
        /// </summary>
        public World.Issue? HotZoneIssue => _hotZoneIssue;

        /// <summary>
        /// Gets whether this hot zone is empty (the top hot version is the same as the last published stable version).
        /// <para>
        /// This is not an issue: the repository has no pending local versions nor CI build.
        /// </para>
        /// </summary>
        public bool IsEmpty => _lastStable == _topHot;

        /// <summary>
        /// Gets the top tag commit. This is greater or equal to <see cref="LastStable"/>.
        /// </summary>
        public TagCommit TopHot => _topHot;

        /// <summary>
        /// Gets the last stable version: this is the common ancestor of the "hot zone" where branch model applies.
        /// <list type="bullet">
        ///     <item>It is most often a published (non "local/") regular version.</item>
        ///     <item>It can be a "+fake" (fake versions are always stable and published).</item>
        ///     <item>It can be a "local/" stable with an associated <see cref="TagCommit.FakeVersion"/>.</item>
        ///     <item>It may be a "+deprecated" stable version (this should not happen: the last - current - version cannot be deprecated).</item>
        /// </list>
        /// </summary>
        public TagCommit LastStable => _lastStable;

        /// <summary>
        /// Gets the <see cref="TagCommitTree"/> for a branch's tip and emits an error on failure.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="branch">The branch to consider.</param>
        /// <returns>The tree or null if <see cref="LastStable"/> is not reachable from <paramref name="branch"/>.</returns>
        public TagCommitTree? GetRequiredTagCommitTree( IActivityMonitor monitor, Branch branch )
        {
            var t = GetTagCommitTree( branch.Tip );
            if( t == null )
            {
                monitor.Error( ActivityMonitor.Tags.ToBeInvestigated,
                               $"Unable to get tag commit tree from branch '{branch.CanonicalName}' to {_lastStable} in '{_info.Repo.DisplayPath}'." );
            }
            return t;
        }

        /// <summary>
        /// Gets the <see cref="TagCommitTree"/> for a commit and emits an error on failure.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="commit">The commit to consider.</param>
        /// <returns>The tree or null if <see cref="LastStable"/> is not reachable from <paramref name="commit"/>.</returns>
        public TagCommitTree? GetRequiredTagCommitTree( IActivityMonitor monitor, Commit commit )
        {
            var t = GetTagCommitTree( commit );
            if( t == null )
            {
                monitor.Error( ActivityMonitor.Tags.ToBeInvestigated,
                               $"Unable to get tag commit tree from commit '{commit.Sha.AsSpan( 0, 7 )}' to {_lastStable} in '{_info.Repo.DisplayPath}'." );
            }
            return t;
        }

        /// <summary>
        /// Gets the <see cref="TagCommitTree"/> for the given commit.
        /// </summary>
        /// <param name="tip">The starting commit that should have <see cref="LastStable"/> in its parents.</param>
        /// <returns>The tree or null if <see cref="LastStable"/> is not reachable from <paramref name="tip"/>.</returns>
        public TagCommitTree? GetTagCommitTree( Commit tip )
        {
            if( _commitTrees != null )
            {
                if( _commitTrees.TryGetValue( tip.Sha, out var cached ) )
                {
                    return cached;
                }
            }
            else
            {
                _commitTrees = new Dictionary<string, TagCommitTree?>();
            }
            var t = CreateTagCommitTree( tip );
            _commitTrees.Add( tip.Sha, t );
            return t;
        }

        TagCommitTree? CreateTagCommitTree( Commit tip )
        {
            // Why are we NOT using:
            //
            // _info.Repo.GitRepository.Repository.Commits.QueryBy( new CommitFilter() { IncludeReachableFrom = tip, ExcludeReachableFrom = _lastStable } );
            //
            // ...to obtain the set of commits and then use it as a filter?
            //
            // 1 - Because if a path from tip doesn't contain LastStable (when an "old" commit has been merged), we'll get all the commits
            //     to the very first one.
            // 2 - Because libgit2 (as well as git, see https://git-scm.com/docs/git-rev-list#Documentation/git-rev-list.txt-Defaultmode) prunes
            //     the graph based on the Content SHA (TREESAME): when playing with "empty commits", we take the risk to miss parents.
            //
            // So we use the Parents and 2 mechanisms help us shorten the walk:
            //  1) when LastStable is met, this stops the walk.
            //  2) when the TagCommit version (if it exists) is lower than the LastStable we stop the walk.
            //
            // We walk until a TagCommit is found and if it's in the hot zone (i.e. greater than LastStable) collect it and
            // continue until the LastStable is met (and finalize the collection with the LastStable).
            // TagCommits not in the hot zone (lower than LastStable) are lost because there is no interest to keep them even
            // for TagCommitTree.GetVersionChange implementation: if no hot zone TagCommits introduce a Major change, we need
            // to re-walk the graph from Tip to any TagCommit (hot zone or not) to analyze their commit messages.
            //
            // To avoid another walk for TagCommitTree.GetVersionChange, we capture the "head commits here": it only costs
            // a List<Commit>.
            //
            var collector = new List<(TagCommit, int)>();
            // This avoids reprocessing the same commit (through merge commits).
            var commitSeen = new HashSet<string>();
            // The commits from Tip to any TagCommits.
            var headCommits = new List<Commit>();

            // The walk is breadth-first by level (a "0-1 BFS"): an untagged commit's parents stay at its level and are
            // processed first, a tagged commit's parents are one level up and wait for the current level to be over.
            // The commits are therefore processed in increasing level order: the collector is sorted by level (what
            // TagCommitTree.GetBestBuildFor relies on) and a commit reachable along more than one path is seen first
            // with its smallest level. This is what a merge commit whose parents both lead to version tags requires
            // (a synchronized branch merges the builds of its parent): both parents are walked level by level.
            var queue = new LinkedList<(Commit, int)>();
            queue.AddFirst( (tip, 0) );
            do
            {
                var (c, l) = queue.First!.Value;
                queue.RemoveFirst();
                int nextL = Collect( _info, commitSeen, _lastStable, c, l, collector, headCommits );
                if( nextL == l )
                {
                    // Reversed so that the first parent is processed first.
                    foreach( var p in c.Parents.Reverse() )
                    {
                        queue.AddFirst( (p, nextL) );
                    }
                }
                else if( nextL > l )
                {
                    foreach( var p in c.Parents )
                    {
                        queue.AddLast( (p, nextL) );
                    }
                }
            }
            while( queue.Count > 0 );

            Throw.DebugAssert( collector.Count == 0 || collector.Select( x => x.Item2 ).IsSortedLarge() );

            return collector.Count == 0
                    ? null
                    : new TagCommitTree( this, tip, collector, headCommits );

            static int Collect( VersionTagInfo info,
                                 HashSet<string> commitSeen,
                                 TagCommit lastStable,
                                 Commit c,
                                 int level,
                                 List<(TagCommit, int)> collector,
                                 List<Commit> headCommits )
            {
                // Seen first: the LastStable is collected once, with its smallest level.
                if( !commitSeen.Add( c.Sha ) )
                {
                    return -1;
                }
                if( c.Sha == lastStable.Sha )
                {
                    collector.Add( (lastStable, level) );
                    return -1;
                }
                if( info.TagCommitsBySha.TryGetValue( c.Sha, out var tc ) )
                {
                    // The tagged version must be greater than the last stable but we handle
                    // the +fake case thanks to the relaxed SameStableAs condition (a prerelease or
                    // CI build of the very version the fake declares is smaller than the fake).
                    if( tc.Version > lastStable.Version
                        || (lastStable.IsFakeVersion && lastStable.Version.SameStableAs( tc.Version )) )
                    {
                        collector.Add( (tc, level) );
                        return level + 1;
                    }
                    return -1;
                }
                if( level == 0 ) headCommits.Add( c );
                return level;
            }
        }

        internal bool OnTagCommitRemoved( TagCommit tc )
        {
            _commitTrees?.Clear();
            return tc != _lastStable;
        }
    }

}

