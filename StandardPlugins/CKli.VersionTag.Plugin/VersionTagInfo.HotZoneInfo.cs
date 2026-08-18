using CK.Core;
using CKli.Core;
using LibGit2Sharp;
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
                    if( !fake.IsStableRoughBaseOf( topHot.Version ) )
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
        ///     <item>It can be a "+deprecated" stable version (deprecated versions are always published).</item>
        ///     <item>It can be a "local/" stable with an associated <see cref="TagCommit.FakeVersion"/>.</item>
        /// </list>
        /// </summary>
        public TagCommit LastStable => _lastStable;

        /// <summary>
        /// Gets all the reachable <see cref="TagCommit"/> from the <paramref name="start"/> up to <see cref="LastStable"/>
        /// with their 0-based increasing level from the first tagged commit found.
        /// <para>
        /// This is a breadth-first traversal. Some consecutive levels may be the same: in that case, the ambiguity must
        /// be resolved by considering the versions (for example, a CI version vs. a non-CI version, an "alpha" vs. a "romeo," etc.).
        /// </para>
        /// </summary>
        /// <param name="start">The commit from which the hot <see cref="TagCommit"/> must be retrieved.</param>
        /// <param name="maxLevel">Optional maximal level to retrieve.</param>
        /// <param name="maxCount">Optional maximal number of commits to retrieve (regardless of the level).</param>
        /// <returns>The versioned tagged commits with their 0-based level.</returns>
        public IReadOnlyList<(TagCommit T, int Level)> CreateTagCommitTreeContent( Commit start, int maxLevel = -1, int maxCount = 0 )
        {
            // Why are we NOT using:
            //
            // _info.Repo.GitRepository.Repository.Commits.QueryBy( new CommitFilter() { IncludeReachableFrom = start, ExcludeReachableFrom = _lastStable } );
            //
            // To obtain the set of commits and then use it as a filter?
            //
            // Because libgit2 (as well as git, see https://git-scm.com/docs/git-rev-list#Documentation/git-rev-list.txt-Defaultmode) prunes the graph
            // based on the Content SHA (TREESAME): when playing with "empty commits", we take the risk to miss parents.
            //
            // So we use the Parents and 3 mechanisms help us shorten the walk:
            //  1) when LastStable is met, this stops the walk.
            //  2) the TagCommit version (if it exists) must be greater to the LastStable otherwise we stop the walk.
            //
            // The fact is that the following code can produce TagCommits that don't have LastStable in their ancestors. This 
            // means that the LastStable is not a "full synchronization point" in the graph, that some branches have not been
            // resynchronized on it before being merged in our "tip" commit history (this is where 2 above kicks in: versions older than
            // LastStable are rejected).
            //
            var collector = new List<(TagCommit, int)>();
            var commitSeen = new HashSet<string>();

            var stack = new Stack<(Commit,int)>();
            stack.Push( (start, 0) );

            do
            {
                var (c,l) = stack.Pop();
                int nextL = Collect( _info, commitSeen, _lastStable, c, l, collector );
                if( maxCount > 0 && collector.Count >= maxCount )
                {
                    break;
                }
                if( nextL >= 0 && (maxLevel == -1 || nextL <= maxLevel) )
                {
                    foreach( var p in start.Parents )
                    {
                        stack.Push( (p, nextL) );
                    }
                }
            }
            while( stack.Count > 0 );
            return collector;

            static int Collect( VersionTagInfo info,
                                 HashSet<string> commitSeen,
                                 TagCommit lastStable,
                                 Commit c,
                                 int level,
                                 List<(TagCommit, int)> collector )
            {
                if( c.Sha == lastStable.Sha )
                {
                    collector.Add( (lastStable, level) );
                    return -1;
                }
                if( !commitSeen.Add( c.Sha ) )
                {
                    return -1;
                }
                if( info.TagCommitsBySha.TryGetValue( c.Sha, out var tc ) )
                {
                    // Allow here the version to be "roughly based" on the lastStable when
                    // the lastStable is a +fake (this is not the case if the lastStable is
                    // a "local/" associated to a FakeVersion i.e. when IsOrHasFakeVersion is true).
                    if( lastStable.Version < tc.Version
                        || (lastStable.IsFakeVersion && !lastStable.Version.IsStableRoughBaseOf( tc.Version )) )
                    {
                        return -1;
                    }
                    collector.Add( (tc, level) );
                    return level + 1;
                }
                return level;
            }
        }


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

            var stack = new Stack<(Commit, int)>();
            stack.Push( (tip, 0) );
            do
            {
                var (c, l) = stack.Pop();
                int nextL = Collect( _info, commitSeen, _lastStable, c, l, collector, headCommits );
                if( nextL >= 0 )
                {
                    foreach( var p in c.Parents )
                    {
                        stack.Push( (p, nextL) );
                    }
                }
            }
            while( stack.Count > 0 );

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
                if( c.Sha == lastStable.Sha )
                {
                    collector.Add( (lastStable, level) );
                    return -1;
                }
                if( !commitSeen.Add( c.Sha ) )
                {
                    return -1;
                }
                if( info.TagCommitsBySha.TryGetValue( c.Sha, out var tc ) )
                {
                    // The tagged version must be greater than the last stable but we handle
                    // the +fake case thanks to the relaxed IsStableRoughBaseOf condition.
                    if( tc.Version > lastStable.Version
                        || (lastStable.IsFakeVersion && lastStable.Version.IsStableRoughBaseOf( tc.Version )) )
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

