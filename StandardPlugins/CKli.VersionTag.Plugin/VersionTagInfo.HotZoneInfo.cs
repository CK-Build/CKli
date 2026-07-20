using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LogLevel = CK.Core.LogLevel;


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
        public IReadOnlyList<(TagCommit T, int Level)> CreateTagCommitTree( Commit start, int maxLevel = -1, int maxCount = 0 )
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

        /// <summary>
        /// Gets the top most versioned commit (the "last build") from a hot branch down to <see cref="LastStable"/>.
        /// <see cref="HotBranch.Exists"/> must be true (this doesn't call <see cref="BranchModelInfo.GetClosestExistingBranch(BranchName)"/>).
        /// <para>
        /// This can always return null and emit a "Build required" error. The returned version may be a "+deprecated" or a "+fake" (this
        /// must be handled by the caller) or may be null when <paramref name="allowFallback"/> is false.
        /// </para>
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="branch">The branch to lookup (for which the <see cref="HotBranch.GitBranch"/> must exist).</param>
        /// <param name="allowCI">Whether CI versions can be returned.</param>
        /// <param name="allowFallback">Whether versions from parent branches (up to the stable root) can be returned.</param>
        /// <param name="notFoundErrorLevel">
        /// Log level to use when no versioned commit can be found.
        /// This cannot happen if <paramref name="allowFallback"/> is true: the <see cref="LastStable"/> is the ultimate fallback
        /// and is, by design, always available.
        /// </param>
        /// <returns>The commit on success, null on error or when no versioned commit can be found.</returns>
        public TagCommit? GetLastBuild( IActivityMonitor monitor,
                                        HotBranch branch,
                                        bool allowCI,
                                        bool allowFallback,
                                        LogLevel notFoundErrorLevel = LogLevel.Error )
        {
            Throw.CheckArgument( branch.Exists );

            if( !DoGetLastBuild( branch,
                                 allowCI,
                                 allowFallback,
                                 out var candidates,
                                 out var buildRequired,
                                 out var lastBuild ) )
            {
                monitor.Error( $"A build of the branch '{buildRequired}' in '{branch.Repo.DisplayPath}' is required." );
                return null;
            }
            if( lastBuild == null && monitor.ShouldLogLine( notFoundErrorLevel, null, out var traits ) )
            {
                var b = (allowCI ? branch.GitDevBranch : null) ?? branch.GitBranch;
                var header = $"Unable to get versioned commit for branch '{branch.BranchName}' in '{_info.Repo.DisplayPath}' ({(allowCI ? "in" : "ex")}cluding \".ci\" versions).";
                var fromTo = $"between '{b.Tip.Id.Sha.AsSpan( 0, 7 )} {b.Tip.MessageShort}' and '{LastStable.Commit.Id.Sha.AsSpan( 0, 7 )} {LastStable.Commit.MessageShort}'";
                if( candidates.Count > 0 )
                {
                    monitor.UnfilteredLog( notFoundErrorLevel | LogLevel.IsFiltered, traits, $"""
                                {header}
                                Considering {candidates.Count} candidates {fromTo}:
                                {candidates.Select( tc => tc.T.Version.ToString() ).Concatenate()}
                                """, error: null );
                }
                else
                {
                    monitor.UnfilteredLog( notFoundErrorLevel | LogLevel.IsFiltered, traits, $"""
                                {header}
                                There is no versioned commit {fromTo}.
                                """, error: null );
                }
            }
            return lastBuild;
        }

        /// <summary>
        /// Gets the top most versioned commit (the "last build") from a hot branch down to <see cref="LastStable"/>.
        /// <see cref="HotBranch.Exists"/> must be true (this doesn't call <see cref="BranchModelInfo.GetClosestExistingBranch(BranchName)"/>).
        /// </summary>
        /// <param name="branch">The branch to lookup (for which the <see cref="HotBranch.GitBranch"/> must exist).</param>
        /// <param name="allowCI">Whether CI versions can be returned.</param>
        /// <param name="buildRequired">On failure, contains the branch name that must be built to resolve an ambiguous last build.</param>
        /// <param name="lastBuild">On success, the last build commit of the branch.</param>
        /// <returns>True on success, false if </returns>
        public bool TryGetLastBuild( HotBranch branch,
                                     bool allowCI,
                                     [NotNullWhen( false )] out BranchName? buildRequired,
                                     [NotNullWhen(true)] out TagCommit? lastBuild )
        {
            Throw.CheckArgument( branch.Exists );
            var b = (allowCI ? branch.GitDevBranch : null) ?? branch.GitBranch;
            var candidates = CreateTagCommitTree( b.Tip );
            return DoGetLastBuild( branch, allowCI, allowFallback: true, out _, out buildRequired, out lastBuild );
        }

        bool DoGetLastBuild( HotBranch branch,
                             bool allowCI,
                             bool allowFallback,
                             out IReadOnlyList<(TagCommit T, int Level)> candidates,
                             [NotNullWhen( false )] out BranchName? buildRequired,
                             out TagCommit? lastBuild )
        {
            Throw.DebugAssert( branch.Exists );
            var b = (allowCI ? branch.GitDevBranch : null) ?? branch.GitBranch;
            candidates = CreateTagCommitTree( b.Tip );

            lastBuild = null;
            var branchName = branch.BranchName;

            retry:
            if( !TryFindBest( branch.BranchModelInfo, candidates, branchName, allowCI, out buildRequired, out lastBuild ) )
            {
                return false;
            }
            if( lastBuild == null )
            {
                if( allowFallback && (branchName = branchName.Parent) != null )
                {
                    goto retry;
                }
                Throw.DebugAssert( "We should have found the LastStable.", !allowFallback );
                return true;
            }
            return true;

            static bool TryFindBest( BranchModelInfo info,
                                     IReadOnlyList<(TagCommit T, int Level)> candidates,
                                     BranchName branchName,
                                     bool allowCI,
                                     [NotNullWhen( false )] out BranchName? buildRequired,
                                     out TagCommit? lastBuild )
            {
                buildRequired = null;
                lastBuild = null;
                int level = 0;
                for( int i = 0; i < candidates.Count; i++ )
                {
                    if( !TryFindBestInLevel( info, candidates, branchName, ref i, level, allowCI, out buildRequired, out lastBuild ) )
                    {
                        return false;
                    }
                    if( lastBuild != null )
                    {
                        return true;
                    }
                    ++level;
                }
                return true;

                static bool TryFindBestInLevel( BranchModelInfo modelInfo,
                                                IReadOnlyList<(TagCommit T, int Level)> candidates,
                                                BranchName branchName,
                                                ref int i,
                                                int level,
                                                bool allowCI,
                                                [NotNullWhen(false)]out BranchName? buildRequired,
                                                out TagCommit? lastBuild )
                {
                    Throw.DebugAssert( candidates[i].Level == level );
                    lastBuild = Filter( candidates, i, allowCI );
                    while( ++i < candidates.Count && candidates[i].Level == level )
                    {
                        var newOne = Filter( candidates, i, allowCI );
                        if( newOne == null ) continue;

                        // Could it be that simple?
                        //
                        // if( best == null || best.Version < newOne.Version )
                        // {
                        //    best = newOne;
                        // }
                        //
                        // Not really.
                        //
                        // Let's say that branch is the "romeo" configured with any link type other than Manual below "zulu" that also exists:
                        // Release: "zulu |> romeo", CI:  "zulu -> romeo" or Full "zulu" => "romeo".
                        //
                        // The fact that branch is "romeo" here means that a "lowest" XXX branch configured with "Release" or "CI" wants
                        // to be synchronized (XXX can be "papa"..."alpha" or an "explo/" branch based on "romeo").
                        //
                        // Scenario 1:
                        // A "1.0.1-zulu" (that only brings a fix) and a "1.1.0-romeo" (that implies a minor enhancement) both exist.
                        // Once synchronized, the "1.0.1-zulu" is merged into the romeo branch. A merge commit has "1.0.1-zulu" and "1.1.0-romeo"
                        // as parents. If a build of the romeo branch is done, a new "1.1.0-romeo.1" will be created that covers the 2 other ones:
                        // this up-to-date romeo build will be retained.
                        // Even if no build is done, by returning the greatest version the (current) "romeo" is returned and this is perfect.
                        //
                        // Scenario 2 (reverts the "natural" previous order):
                        // A "1.1.0-zulu" (zulu implies a minor enhancement) exists, romeo with a fix or a minor: "1.1.0-romeo" or "1.0.1-romeo"
                        // will always be lower than the zulu version (because the pre-release name).
                        // If a build of the romeo branch is done, a new "1.1.0-romeo.1" will be created that covers the 2 other ones: this up-to-date romeo
                        // build will be retained (because of the TagCommit/Level returned by GetTagCommitTree).
                        // But if no build is done, returning the greatest version will retain the "zulu" version... and that is not what we want (this
                        // code base doesn't contain the "romeo" code at all).
                        //
                        // Does it mean that we should always return a "romeo" version here (because the branch is "romeo")?
                        // Actually yes. Another option would be to raise an error here "A build of the branch 'romeo' is required before building 'XXX'."
                        // but this may be irritating: XXX branch can still be built based on the last produced "romeo" until the developer decides
                        // to produce a new "romeo".
                        //
                        // What happens when a branch is closed? (Note that when close --discard is done, there's no issue.)  
                        // Closing "romeo": the XXX branch becomes based on "zulu" and the "romeo" branch has been integrated into "zulu".
                        // When XXX is synchronized in this scenario, it will consider the "1.1.0-zulu" version... and this is bad!
                        // ==> Closing means that once the subordinated branch has been merged into its base, then a build should be made
                        //     on the base branch (to integrate the new code). 
                        // In this case we can only raise the error "A build of the branch 'zulu' is required before building 'XXX'."
                        // (Because continuing to return the "romeo" is obviously bad - this version doesn't "exist" anymore - and returning
                        // the "zulu" will forget the "romeo" code that has been integrated).
                        //
                        // Can we easily detect the 2 scenarii? Yes!
                        // When a version from an "alien branch" is met (a VersionKind/ExploratoryName that is not the same as the requested
                        // branch name) then either:
                        // - the BranchName still exists: it is a synchronization (waiting for its future unifying build).
                        // - the BranchName doesn't exist: it is a closed branch that has been integrated, we raise the "build required" error.
                        //

                        if( branchName.Match( newOne.Version ) )
                        {
                            // Regular case: the branch is the one of the version.
                            if( lastBuild == null || lastBuild.Version < newOne.Version )
                            {
                                lastBuild = newOne;
                            }
                        }
                        else
                        {
                            // Is it useful to determine whether the matching branch is "above" (in the case of a sync) or
                            // "below" (in the case of a merge)?
                            // No. Because branches can be reopened. What matters here is that a version cannot logically exists because its
                            // branch is dead. A branch that has been closed and reopened doesn't change anything: the branch exists, it
                            // must be ignored here. 
                            //
                            if( modelInfo.Namespace.Branches.Any( b => b.Match( newOne.Version ) ) )
                            {
                                // The version belongs to another branch that still exists.
                                // Simply ignore it.
                            }
                            else
                            {
                                // The version belongs to a closed branch.
                                buildRequired = branchName;
                                return false;
                            }
                        }
                    }
                    buildRequired = null;
                    return true;

                    static TagCommit? Filter( IReadOnlyList<(TagCommit T, int Level)> candidates, int i, bool allowCI )
                    {
                        var r = candidates[i].T;
                        return allowCI || !r.Version.IsCI ? r : null;
                    }
                }


            }
        }
    }

}

