using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;


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

        HotZoneInfo( VersionTagInfo info, TagCommit lastStable, TagCommit topHot, World.Issue? hotZoneIssue )
        {
            _info = info;
            _lastStable = lastStable;
            _topHot = topHot;
            _hotZoneIssue = hotZoneIssue;
        }

        internal static HotZoneInfo Create( IActivityMonitor monitor, VersionTagInfo info, TagCommit lastStable, TagCommit topHot )
        {
            World.Issue? hotZoneIssue = null;
            var hotSupremum = SVersion.Create( lastStable.Version.Major + 1, 0, 0 );
            if( topHot.Version >= hotSupremum )
            {
                var message = $"""
                              The greatest version tag '{topHot.Version.ParsedText}' cannot be greater or equal to 'v{lastStable.Version.Major + 1
                              }.0.0' because the last stable version is '{lastStable.Version.ParsedText}'.
                              This should be fixed manually.
                              """;

                monitor.Warn( $"Hot zone issue in '{info.Repo.DisplayPath}': {message}" );
                hotZoneIssue = World.Issue.CreateManual( "Hot zone issue detected.", info.Repo.World.ScreenType.Text( message ), info.Repo );
            }
            return new HotZoneInfo( info, lastStable, topHot, hotZoneIssue );
        }

        /// <summary>
        /// Gets a manual issue if <see cref="TopHot"/> is greater or equal to the next major of the last stable version.
        /// </summary>
        public World.Issue? HotZoneIssue => _hotZoneIssue;

        /// <summary>
        /// Gets whether this hot zone is empty (the top hot version is the same as the last stable version).
        /// <para>
        /// This is not an issue: the repository has no pending pre-release version nor CI build.
        /// </para>
        /// </summary>
        public bool IsEmpty => _lastStable == _topHot;

        /// <summary>
        /// Gets the top tag commit. This is greater or equal to <see cref="LastStable"/>.
        /// </summary>
        public TagCommit TopHot => _topHot;

        /// <summary>
        /// Gets the last stable version: this is the common ancestor of the "hot zone" where branch model applies.
        /// <para>
        /// This can be a "+fake" or a "+deprecated" version (<see cref="TagCommit.IsRegularVersion"/> can be false) and
        /// <see cref="TagCommit.BuildContentInfo"/> may be null.  
        /// </para>
        /// </summary>
        public TagCommit LastStable => _lastStable;

        /// <summary>
        /// Gets the reachable <see cref="TagCommit"/> from the <paramref name="start"/> up to <see cref="LastStable"/>
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
        public IReadOnlyList<(TagCommit T, int Level)> GetTagCommitTree( Commit start, int maxLevel = -1, int maxCount = 0 )
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
            //  2) the time between the LastStable and the commit is checked (with half an hour margin): a too old commit stops the walk.
            //     The margin handles clock drift and/or minor manual changes or adjustments.
            //  3) the TagCommit version (if it exists) must be greater to the LastStable otherwise we stop the walk.
            //
            // The fact is that the following code can produce TagCommits that don't have LastStable in their ancestors. This 
            // means that the LastStable has not been full "synchronization point" in the graph, that some branches have not been
            // resynchronized on it before being merged in our "start" commit history. This is where 2) and 3) above kicks in:
            // too old commits and versions older than LastStable are rejected.
            // 
            var timeLimit = _lastStable.Commit.Committer.When.UtcDateTime.AddMinutes( -30 );
            var collector = new List<(TagCommit, int)>();
            var commitSeen = new HashSet<string>();

            var stack = new Stack<(Commit,int)>();
            stack.Push( (start, 0) );

            do
            {
                var (c,l) = stack.Pop();
                int nextL = Collect( _info, commitSeen, timeLimit, _lastStable, c, l, collector );
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
                                 DateTime timeLimit,
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
                if( c.Committer.When.UtcDateTime < timeLimit || !commitSeen.Add( c.Sha ) )
                {
                    return -1;
                }
                if( info.TagCommitsBySha.TryGetValue( c.Sha, out var tc ) )
                {
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

    }

}

