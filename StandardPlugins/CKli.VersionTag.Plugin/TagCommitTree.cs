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
using static System.Net.Mime.MediaTypeNames;

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
    readonly List<Commit> _headCommits;
    readonly VersionTagInfo.HotZoneInfo _hotZone;
    SVersionChange? _versionChange;

    internal TagCommitTree( VersionTagInfo.HotZoneInfo hotZone, Commit tip, List<(TagCommit, int)> content, List<Commit> headCommits )
    {
        Throw.DebugAssert( content.Count > 0 && content[^1].Item1 == hotZone.LastPublishedStable );
        _hotZone = hotZone;
        _tip = tip;
        _content = content;
        _headCommits = headCommits;
    }

    /// <summary>
    /// Gets the starting commit.
    /// </summary>
    public Commit Tip => _tip;

    /// <summary>
    /// Gets the <see cref="VersionTagInfo.HotZoneInfo.LastPublishedStable"/>.
    /// </summary>
    public TagCommit LastStable => _hotZone.LastPublishedStable;

    /// <summary>
    /// Gets the versioned tag commits with their 0-based increasing level from the first one.
    /// The last one is necessarily <see cref="LastStable"/>.
    /// </summary>
    public IReadOnlyList<(TagCommit, int)> Content => _content;

    /// <summary>
    /// Gets the last build from a <see cref="BranchName"/> or null if not found.
    /// </summary>
    /// <param name="branch">The branch name.</param>
    /// <param name="allowCI">Whether CI versions are allowed.</param>
    /// <param name="allowLocal">Whether <see cref="SVersionExtensions.IsLocal(SVersion)"/> versions must be considered.</param>
    /// <returns>The versioned tag commit or null if not found.</returns>
    public TagCommit? GetLastBuild( BranchName branch, bool allowCI, bool allowLocal )
    {
        return _content.Select( x => x.Item1 ).FirstOrDefault( tc => (allowCI || !tc.Version.IsCI )
                                                                     && (allowLocal || !tc.Version.IsLocal())
                                                                     && branch.Match( tc.Version ) );
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
        int i = 0;
        while( i < _content.Count )
        {
            (c, b) = BestInLevel( _content, branch, ref i, level, allowCI );
            if( c != null ) break;
            ++level;
        }
        Throw.DebugAssert( "We eventually reached LastStable.", c != null && b != null );
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
            }
            while( ++i < content.Count && content[i].Level == level );
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
    /// and conventional commit messages content). Final value is at least <see cref="SVersionChange.Patch"/>.
    /// </param>
    /// <param name="branch">The target branch for which the next version must be produced.</param>
    /// <param name="ciBuild">Whether a CI version must be produced.</param>
    /// <param name="mustAddCommit">
    /// Whether a new commit will be created: this increments the <see cref="SVersion.CINumber"/> (applies only
    /// if <paramref name="ciBuild"/> is true).
    /// </param>
    /// <param name="allowLocal">Whether <see cref="SVersionExtensions.IsLocal(SVersion)"/> versions must be considered.</param>
    /// <returns>The version to create or null on error.</returns>
    public SVersion? ComputeTargetVersion( IActivityMonitor monitor,
                                           ref SVersionChange vChange,
                                           BranchName branch,
                                           bool ciBuild,
                                           bool mustAddCommit,
                                           bool allowLocal )
    {
        // Initial version to use: if we are on a +fake (like 1.5.4+fake, a +fake can only be a stable version), then
        // we eventually want to produce v1.5.4 (not v1.5.5, v1.6.0 or v2.0.0) the initial version is the fake one,
        // not the next one: vChange is useless for +fake!
        SVersion v = LastStable.Version;
        Throw.DebugAssert( "Starting from the stable: no prerelease suffix to cleanup.", v.Prerelease.Length == 0 );
        bool applyVersionIncrement = true;
        if( v.HasFakeMetadata )
        {
            // Since we build, we consider a minimal Patch change.
            if( vChange == SVersionChange.None ) vChange = SVersionChange.Patch;
            // If We are on the +fake that is the InfVersion for the repository,
            // the we must adjust the behavior.
            // Inf is not Min: versions must be strictly greater than InfVersion, so
            // we consider the +fake as a "real" previous version and apply the
            // increment as usual.
            var infVersion = _hotZone.VersionTagInfo.InfVersion;
            applyVersionIncrement = infVersion != null
                                    && infVersion.Major == v.Major
                                    && infVersion.Minor == v.Minor
                                    && infVersion.Patch == v.Patch;
        }
        if( applyVersionIncrement )
        {
            if( vChange < SVersionChange.Major )
            {
                var c = GetVersionChange();
                if( c > vChange ) vChange = c;
                // Ultimately use Patch.
                if( vChange == SVersionChange.None ) vChange = SVersionChange.Patch;
            }
            v = v.SetNextVersionNumbers( vChange );
        }
        // The LastStable version may HasFakeMetadata but may also HasDeprecatedMetadata: we always 
        // clear the metadata (it's a nop when there's no metadata).
        v = v.SetBuildMetaData( null );
        // Now that we have the Major.Minor.Patch, let's add the branch name and its potential
        // prerelease number (if not on the root).
        if( !branch.IsRoot )
        {
            v = v.SetBranchName( branch.Name );
            // Determine the PrereleaseNumber.
            int prereleaseNumber = 0;
            // Are there any previous release with this branch name?
            var lastBuild = GetLastBuild( branch, ciBuild, allowLocal );
            if( lastBuild != null )
            {
                // If yes, then our prerelease number must be incremented
                // unless we are in CI.
                prereleaseNumber = lastBuild.Version.PrereleaseNumber;
                if( !ciBuild ) ++prereleaseNumber;
            }
            // If no release on this branch exist, the prereleaseNumber is 0 (the first one).
            v = v.SetPrereleaseNumber( prereleaseNumber );
        }
        // Finalize with the CI number if required.
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
                // First, use the tags.
                var last = LastStable.Version;
                foreach( var x in _content )
                {
                    if( !last.IsPreviousVersionNumbersOf( x.Item1.Version, out var c ) )
                    {
                        Throw.CKException( $"The '{x.Item1}' doesn't follow LasStable version '{last}'." );
                    }
                    if( c > vChange )
                    {
                        vChange = c;
                        if( vChange == SVersionChange.Major ) break;
                    }
                }
                // If no major change so far, we must analyze the commit's messages.
                // We consider the Tip's ancestors up to any TagCommit. The commits that have
                // a TagCommit are ignored: either they are the top ones from the hot zone and their
                // version changes have been computed above, or they are paths that lead to older
                // versions than the LastStable.
                if( vChange != SVersionChange.Major )
                {
                    foreach( var commit in _headCommits )
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
