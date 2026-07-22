using CK.Core;
using CKli.BranchModel.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Captures the all the reachable <see cref="TagCommit"/> from the <see cref="Tip"/> up to <see cref="LastStable"/>
/// with their 0-based increasing level from the first tagged commit found.
/// <para>
/// This encapsulates 3 fundamental aspects of the branch management in the Hot Zone:
/// <list type="number">
///     <item>
///     This provides the "Last Build" for any <see cref="BranchName"/> regardless of merges with other branches.
///     This is the basics of the branch synchronization for <see cref="BranchLinkType.CI"/> and <see cref="BranchLinkType.Release"/>.
///     </item>
///     <item>
///     This can compute the "Best Build" to use from any <see cref="BranchName"/>.
///     This supports the cross repository dependency resolution.</item>
///     <item>
///     This computes the <see cref="SVersionChange"/> from the <see cref="Tip"/> down to <see cref="LastStable"/>. This is used to compute
///     new version automatically.
///     </item>
/// </list>
/// </para>
/// </summary>
public sealed class TagCommitTree
{
    readonly Commit _tip;
    readonly List<(TagCommit, int)> _content;
    readonly int _zeroLevelCount;
    readonly VersionTagInfo.HotZoneInfo _hotZone;
    readonly ImmutableArray<TagCommit> _topTagCommits;
    HashSet<Commit>? _headCommits;
    SVersionChange? _versionChange;

    internal TagCommitTree( VersionTagInfo.HotZoneInfo hotZone, Commit tip, List<(TagCommit, int)> content, int zeroLevelCount )
    {
        _hotZone = hotZone;
        _tip = tip;
        _content = content;
        _zeroLevelCount = zeroLevelCount;
        _topTagCommits = ImmutableArray.CreateRange( content.Select( x => x.Item1 ).Take( zeroLevelCount ) );
    }

    /// <summary>
    /// Gets the starting commit.
    /// </summary>
    public Commit Tip => _tip;

    /// <summary>
    /// Gets the <see cref="VersionTagInfo.HotZoneInfo.LastStable"/>.
    /// </summary>
    public TagCommit LastStable => _hotZone.LastStable;

    /// <summary>
    /// Gets the versioned tag commits with their 0-based increasing level from the first one.
    /// The last one is necessarily <see cref="LastStable"/>.
    /// </summary>
    public IReadOnlyList<(TagCommit, int)> Content => _content;

    /// <summary>
    /// Gets the level 0 <see cref="TagCommit"/> in <see cref="Content"/>.
    /// </summary>
    public ImmutableArray<TagCommit> TopTagCommits => _topTagCommits;

    /// <summary>
    /// Gets all the commits from <see cref="Tip"/> (included) to the <see cref="TopTagCommits"/> (excluded).
    /// This is empty if Tip appears in the TopTagCommits.
    /// </summary>
    public IReadOnlySet<Commit> HeadCommits
    {
        get
        {
            if( _headCommits == null )
            {
                _headCommits = new HashSet<Commit>();
                var stops = new Commit[_zeroLevelCount];
                for( int i = 0; i < _zeroLevelCount; ++i ) stops[i] = _topTagCommits[i].Commit;
                if( AddParents( stops, _tip, _headCommits ) ) _headCommits.Add( _tip );
                Throw.DebugAssert( _headCommits.Count == 0 || _headCommits.Contains( _tip ) );
            }
            return _headCommits;

            static bool AddParents( Commit[] stops, Commit c, HashSet<Commit> heads )
            {
                bool hasStop = false;
                foreach( var p in c.Parents )
                {
                    if( Array.IndexOf( stops, p ) >= 0 )
                    {
                        hasStop = true;
                    }
                    else if( AddParents( stops, p, heads ) )
                    {
                        heads.Add( p );
                        hasStop = true;
                    }
                }
                return hasStop;
            }
        }
    }

    /// <summary>
    /// Gets the last build from a <see cref="BranchName"/> or null if not found.
    /// </summary>
    /// <param name="branch">The branch name.</param>
    /// <param name="allowCI">Whether CI version are allowed.</param>
    /// <returns>The versioned tag commit or null if not found.</returns>
    public TagCommit? GetLastBuild( BranchName branch, bool allowCI )
    {
        return _content.Select( x => x.Item1 ).FirstOrDefault( tc => (allowCI || !tc.Version.IsCI ) && branch.Match( tc.Version ) );
    }

    /// <summary>
    /// Calls <see cref="GetLastBuildWithFallback(BranchName, bool)"/> on <paramref name="branch"/> and on its parents until
    /// a versioned tag commit is found.
    /// <para>
    /// This follows the <see cref="BranchName.Parent"/> path until <see cref="LastStable"/> is met.
    /// </para>
    /// </summary>
    /// <param name="branch">The branch name.</param>
    /// <param name="allowCI">Whether CI version are allowed.</param>
    /// <returns>The versioned tag commit and the final branch.</returns>
    public (TagCommit C, BranchName Branch) GetLastBuildWithFallback( BranchName branch, bool allowCI )
    {
        var b = branch;
        for( ; ; )
        {
            var c = _content.Select( x => x.Item1 ).FirstOrDefault( tc => (allowCI || !tc.Version.IsCI) && b.Match( tc.Version ) );
            if( c != null ) return (c, b);
            b = b.Parent;
            Throw.DebugAssert( b != null );
        }
    }

    /// <summary>
    /// Gets the build (and its origin branch) that must be used/referenced "from a branch": this is
    /// the heart of the dependency resolution across repositories.
    /// <para>
    /// This tries to find a version with the closest branch among each increasing level in <see cref="Content"/>.
    /// </para>
    /// </summary>
    /// <param name="branch">The starting branch (the "point of view").</param>
    /// <param name="allowCI">Whether CI version are allowed.</param>
    /// <returns>The versioned tag commit and the final branch.</returns>
    public (TagCommit Commit, BranchName Branch) GetBestBuildFor( BranchName branch, bool allowCI )
    {
        TagCommit? c = null;
        BranchName? b = null;
        int level = 0;
        for( int i = 0; i < _content.Count; i++ )
        {
            (c, b) = BestInLevel( _content, branch, ref i, level, allowCI );
            if( c != null ) break;
            ++level;
        }
        Throw.DebugAssert( "We eventually reach LastStable.", c != null && b != null );
        return (c, b);


        static (TagCommit?,BranchName?) BestInLevel( IReadOnlyList<(TagCommit T, int Level)> content,
                                                     BranchName branchName,
                                                     ref int i,
                                                     int level,
                                                     bool allowCI )
        {
            Throw.DebugAssert( content[i].Level == level );
            BranchName? bestB = null;
            TagCommit? bestC = null;
            do
            {
                var newC = Filter( content, i, allowCI );
                if( newC == null ) continue;

                var newB = FindClosestBranch( newC.Version, branchName );
                if( newB == null ) continue;

                // Regular case: the branch is the one of the version.
                if( bestB == null || bestB.Index < newB.Index )
                {
                    bestB = newB;
                    bestC = newC;
                }
                ++i;
            }
            while( i < content.Count && content[i].Level == level );
            return (bestC,bestB);

            static TagCommit? Filter( IReadOnlyList<(TagCommit T, int Level)> candidates, int i, bool allowCI )
            {
                var r = candidates[i].T;
                return allowCI || !r.Version.IsCI ? r : null;
            }
        }

        static BranchName? FindClosestBranch( SVersion v, BranchName branchName )
        {
            var b = branchName;
            do
            {
                if( b.Match( v ) )
                {
                    return b;
                }

                b = b.Parent;
            }
            while( b != null );
            return null;
        }
    }

    public SVersionChange GetVersionChange()
    {
        if( _versionChange is null )
        {
            foreach( var x in _content )
            {
                if( x.Item1.Version )
            }
        }
        return _versionChange.Value;
    }
}
