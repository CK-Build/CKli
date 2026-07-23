using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using LibGit2Sharp;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

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
public sealed partial class TagCommitTree
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
        Throw.DebugAssert( content.Count > 0 && content[^1].Item1 == hotZone.LastStable );
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
    public (TagCommit Commit, BranchName Branch) GetLastBuildWithFallback( BranchName branch, bool allowCI )
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

    /// <summary>
    /// Computes the next version for a branch name based on its last build if it exists.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="vChange">
    /// The current known version change to apply. This can be upgraded by the content of this tree (past released versions
    /// and conventional commit messages content).
    /// </param>
    /// <param name="branch">The target branch for which the next version must be produced.</param>
    /// <param name="ciBuild">Whether a CI version must be produced.</param>
    /// <param name="mustAddCommit">
    /// Whether a new commit will be created: this increments the <see cref="SVersion.CINumber"/>, this applies
    /// only if <paramref name="ciBuild"/> is true.
    /// </param>
    /// <returns></returns>
    public SVersion? ComputeTargetVersion( IActivityMonitor monitor,
                                           ref SVersionChange vChange,
                                           BranchName branch,
                                           bool ciBuild,
                                           bool mustAddCommit )
    {
        if( vChange < SVersionChange.Major )
        {
            var c = GetVersionChange();
            if( c > vChange ) vChange = c;
            // Ultimately use Patch.
            if( vChange == SVersionChange.None ) vChange = SVersionChange.Patch;
        }
        var v = LastStable.Version.SetNextVersionNumbers( vChange );
        if( !branch.IsRoot )
        {
            v = v.SetBranchName( branch.Name );
            // Determine the PrereleaseNumber.
            int prereleaseNumber = 0;
            // Are there any previous release with this branch name?
            var lastBuild = GetLastBuild( branch, ciBuild );
            if( lastBuild != null )
            {
                // If yes, then our prerelease number must be incremented
                // unless we are in CI.
                prereleaseNumber = lastBuild.Version.PrereleaseNumber;
                if( !ciBuild ) ++prereleaseNumber;
            }
            // If no release on this branch exist, the prereleaseNumber 0 is
            // the first one.
            v = v.SetPrereleaseNumber( prereleaseNumber );
        }
        // Determine the CI number.
        if( ciBuild )
        {
            int ciNumber = LastStable.Repo.GitRepository.ComputeCommitDepth( monitor, LastStable.Commit, _tip );
            if( ciNumber < 0 )
            {
                monitor.Error( $"Unable to compute commit depth from branch '{_tip.Sha.AsSpan(0,7)} {_tip.MessageShort}' to the base {LastStable}." );
                return null;
            }
            if( mustAddCommit ) ++ciNumber;
            v = v.SetCINumber( ciNumber, impactStablePatchNumber: false );
        }
        return v;
    }

    /// <summary>
    /// Computes the <see cref="SVersionChange"/> between <see cref="Tip"/> and <see cref="LastStable"/>.
    /// <para>
    /// This can be <see cref="SVersionChange.None"/> if the Tip's and LastStable's Git tree are the same
    /// and there is no tag commits.
    /// </para>
    /// </summary>
    /// <returns>The version change.</returns>
    public SVersionChange GetVersionChange()
    {
        if( _versionChange is null )
        {
            SVersionChange vChange = SVersionChange.None;
            if( _tip.Sha != LastStable.Commit.Sha || _content.Count != 1 )
            {
                // First, use the tags. If LastStable is a +fake we cannot conclude anything.
                if( !LastStable.IsFakeVersion )
                {
                    foreach( var x in _content )
                    {
                        var c = LastStable.Version.FromNextVersion( x.Item1.Version );
                        if( c > vChange )
                        {
                            vChange = c;
                            if( vChange == SVersionChange.Major ) break;
                        }
                    }
                }
                if( vChange != SVersionChange.Major )
                {
                    foreach( var commit in HeadCommits )
                    {
                        var c = DetectVersionChange( commit, noNone: true );
                        if( c > vChange )
                        {
                            vChange = c;
                            if( vChange == SVersionChange.Major ) break;
                        }
                    }
                }
            }
            _versionChange = vChange;
        }
        return _versionChange.Value;

        static SVersionChange DetectVersionChange( Commit c, bool noNone = true )
        {
            var message = c.Message;
            // Loosely following the spec here. For us, any appearance of the
            // BREAKING CHANGE anywhere is enough (because of the upper case).
            if( message.Contains( "BREAKING CHANGE", StringComparison.Ordinal )
                || message.Contains( "BREAKING-CHANGE", StringComparison.Ordinal ) )
            {
                return SVersionChange.Major;
            }

            var m = ConventionalCommitHeader().Match( message );
            if( m.Success )
            {
                // The ! after the type/scope.
                if( m.Groups[3].ValueSpan.Length > 0 )
                {
                    return SVersionChange.Major;
                }
                var type = m.Groups[1].ValueSpan;
                return type switch
                {
                    "feat" => SVersionChange.Minor,
                    "merge" or "none" => noNone ? SVersionChange.Patch : SVersionChange.None,
                    _ => SVersionChange.Patch
                };
            }
            // Consider that merge commits are None.
            return !noNone && c.Parents.Count() > 1
                    ? SVersionChange.None
                    : SVersionChange.Patch;
        }

    }

    [GeneratedRegex( @"^(?<1>\w+)(?:\((?<2>[^()]+)\))?(?<3>!)?:", RegexOptions.CultureInvariant )]
    private static partial Regex ConventionalCommitHeader();
}
