using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// <see cref="RepoInfo"/> for version tags related information.
/// </summary>
public sealed partial class VersionTagInfo : RepoInfo
{
    readonly VersionTagPlugin _versionTagPlugin;
    readonly SVersion? _infVersion;
    readonly SVersion? _supVersion;

    [AllowNull] Dictionary<SVersion, TagCommit> _v2C;
    [AllowNull] IReadOnlyList<Tag> _removableTags;
    HotZoneInfo? _hotZone;
    //
    // The +invalid tags are already handled, kept here but not used anymore:
    // one day, may be, we'll remove them but before, we must ensure that the hidden
    // version tags are removed in other repositories...
    //
    Dictionary<SVersion, (SVersion V, Tag T)>? _invalidTags;
    List<((SVersion V, Tag T) T1, (SVersion V, Tag T) T2, TagConflict C)>? _tagConflicts;
    List<(SVersion V, Tag T)>? _badDeprecatedTags;
    List<(SVersion V, Tag T)>? _lightweightOrUnreadableRegularTags;
    bool _hasIssue;
    // Lazy initialization.
    Dictionary<string, TagCommit>? _sha2C;
    ImmutableArray<TagCommit> _lastStables;
    ImmutableArray<TagCommit> _lastMajorMinorStables;

    internal VersionTagInfo( VersionTagPlugin plugin,
                             Repo repo,
                             SVersion? infVersion,
                             SVersion? supVersion )
           : base( repo )
    {
        _versionTagPlugin = plugin;
        _infVersion = infVersion;
        _supVersion = supVersion;
    }

    internal void Initialize( HotZoneInfo? hotZone,
                              Dictionary<SVersion, TagCommit> v2c,
                              List<Tag>? removableTags,
                              Dictionary<SVersion, (SVersion V, Tag T)>? invalidTags,
                              List<((SVersion V, Tag T) T1, (SVersion V, Tag T) T2, TagConflict C)>? tagConflicts,
                              List<(SVersion V, Tag T)>? badDeprecatedTags,
                              List<(SVersion V, Tag T)>? lightweightOrUnreadableRegularTags )
    {
        _hotZone = hotZone;
        _v2C = v2c;
        _removableTags = removableTags ?? [];
        _invalidTags = invalidTags;
        _tagConflicts = tagConflicts;
        _badDeprecatedTags = badDeprecatedTags;
        _lightweightOrUnreadableRegularTags = lightweightOrUnreadableRegularTags;
        _hasIssue = hotZone == null || hotZone.HotZoneIssue != null
                     || lightweightOrUnreadableRegularTags != null
                     || tagConflicts != null
                     || badDeprecatedTags != null;
    }

    /// <summary>
    /// Gets the optional lower limit of the versions configured for this Repo in the VersionTag plugin configuration.
    /// <para>
    /// This is an infimum, not a minimum: when not null, considered versions are strictly greater than this value.
    /// </para>
    /// </summary>
    public SVersion? InfVersion => _infVersion;

    /// <summary>
    /// Gets the optional upper limit of the versions configured for this Repo in the VersionTag plugin configuration.
    /// <para>
    /// This is a supremum, not a maximum: when not null, considered versions are strictly lower than this value.
    /// </para>
    /// </summary>
    public SVersion? SupVersion => _supVersion;

    /// <summary>
    /// Gets whether this repository has "annoying" issues related to its version tags.
    /// <para>
    /// Non empty <see cref="RemovableTags"/> is not considered annoying.
    /// </para>
    /// </summary>
    [MemberNotNullWhen( false, nameof( HotZone ) )]
    public override bool HasIssue => _hasIssue;

    /// <summary>
    /// Gets the last stable versions from the last stable one to the oldest one.
    /// <para>
    /// <see cref="TagCommit.IsRegularVersion"/> may be false ("+fake" and "+deprecated" appear here).
    /// </para>
    /// <para>
    /// When this is empty, then <see cref="HotZone"/> is null and <see cref="HasIssue"/> is true: a first stable version (greater than 
    /// <see cref="InfVersion"/>) should be produced to fix this. This fix is handled by the Build plugin (if the root "stable" branch exists).
    /// </para>
    /// <para>
    /// This can be updated when a TagCommit is removed or inserted.
    /// </para>
    /// </summary>
    public ImmutableArray<TagCommit> LastStables
    {
        get
        {
            if( _lastStables.IsDefault )
            {
                _lastStables = _v2C.Values.Where( tc => tc.Version.IsStable ).Order().ToImmutableArray();
                Throw.DebugAssert( (_lastStables.Length >  0) == (_hotZone != null) );
            }
            return _lastStables;
        }
    }

    /// <summary>
    /// Gets the filtered set of <see cref="LastStables"/> with the maximal <see cref="SVersion.Patch"/>.
    /// <para>
    /// <see cref="TagCommit.IsRegularVersion"/> may be false ("+fake" and "+deprecated" versions appear here).
    /// </para>
    /// </summary>
    public ImmutableArray<TagCommit> LastMajorMinorStables
    {
        get
        {
            if( _lastMajorMinorStables.IsDefault )
            {
                var c = _hotZone?.LastStable;
                if( c == null )
                {
                    _lastMajorMinorStables = [];
                }
                else
                {
                    var b = ImmutableArray.CreateBuilder<TagCommit>();
                    b.Add( c );
                    var cV = c.Version;
                    foreach( var tc in LastStables )
                    {
                        var v = tc.Version;
                        if( v.Major != cV.Major || v.Minor != cV.Minor )
                        {
                            b.Add( tc );
                            cV = v;
                        }
                    }
                    _lastMajorMinorStables = b.DrainToImmutable();
                }
            }
            return _lastMajorMinorStables;
        }
    }

    /// <summary>
    /// Gets the hot zone information. Never null if <see cref="HasIssue"/> is false.
    /// <para>
    /// This is not null as soon as a <see cref="HotZoneInfo.LastStable"/> exists.
    /// </para>
    /// </summary>
    public HotZoneInfo? HotZone => _hotZone;

    /// <summary>
    /// Gets a <see cref="TagCommit"/> for a version.
    /// <para>
    /// For "ci.0" version (when <see cref="SVersion.CINumber"/> is 0), this returns the commit of the base version.
    /// The found base may have a <see cref="TagCommit.CI0VersionTag"/>.
    /// </para>
    /// </summary>
    /// <param name="version">The version to find.</param>
    /// <returns>The tag commit if it exists, null otherwise.</returns>
    public TagCommit? GetTagCommit( SVersion version )
    {
        if( _v2C.TryGetValue( version, out var tc ) )
        {
            return tc;
        }
        if( version.CINumber == 0 )
        {
            var vBase = version.SetCINumber( -1 );
            if( _v2C.TryGetValue( vBase, out tc ) )
            {
                return tc;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets a <see cref="TagCommit"/> for a version.
    /// <para>
    /// For "ci.0" version (when <see cref="SVersion.CINumber"/> is 0, this locates the commit of the base version.
    /// The found base may have a <see cref="TagCommit.CI0VersionTag"/>.
    /// </para>
    /// </summary>
    /// <param name="version">The version to find.</param>
    /// <param name="tagCommit">On success, the found tag commit.</param>
    /// <returns>True in success, false if the version doesn't exist.</returns>
    public bool TryGetTagCommit( SVersion version, [NotNullWhen( true )] out TagCommit? tagCommit ) => (tagCommit = GetTagCommit( version )) != null;

    /// <summary>
    /// Gets the versioned tag commit indexed by their <see cref="TagCommit.Sha"/>.
    /// </summary>
    public IReadOnlyDictionary<string, TagCommit> TagCommitsBySha
    {
        get
        {
            if( _sha2C == null )
            {
                _sha2C = new Dictionary<string, TagCommit>( _v2C.Count );
                foreach( var tc in _v2C.Values )
                {
                    _sha2C.Add( tc.Sha, tc );
                }
            }
            return _sha2C;
        }
    }

    /// <summary>
    /// Gets all the <see cref="TagCommit"/>.
    /// </summary>
    public IEnumerable<TagCommit> AllTagCommits => _v2C.Values;

    /// <summary>
    /// Gets the tags that can be removed (at least locally).
    /// </summary>
    public IReadOnlyList<Tag> RemovableTags => _removableTags;

    /// <summary>
    /// Gets whether tag conflicts have been found.
    /// </summary>
    public bool HasTagConflicts => _tagConflicts != null;

    /// <summary>
    /// Gets the version tags from which the <see cref="BuildContentInfo"/> cannot be read.
    /// It may be because the tag is not an annotated tag or its <see cref="TagAnnotation.Message"/> is not
    /// parsable by <see cref="BuildContentInfo.TryParse(ReadOnlySpan{char}, out BuildContentInfo?)"/>.
    /// <para>
    /// The Build plugin exposes this as an automatic issue by offering to compile the commits to restore
    /// their build content info.
    /// </para>
    /// </summary>
    public IReadOnlyList<(SVersion V, Tag T)>? LightweightOrUnreadableRegularTags => _lightweightOrUnreadableRegularTags;

    /// <summary>
    /// Checks that <see cref="HasTagConflicts"/> is false or emits an error that invites
    /// the user to use 'ckli issue' and returns false. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True if no tag conflict exist, false otherwise.</returns>
    public bool CheckNoTagConflicts( IActivityMonitor monitor )
    {
        if( _tagConflicts != null )
        {
            monitor.Error( $"""
                    There are existing tag conflicts in '{Repo.DisplayPath}'. They must be (manually) fixed before.
                    Use 'ckli issue' to see them.
                    """ );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Finds the first version tag in a commit list.
    /// </summary>
    /// <param name="commits">The list of commits to lookup.</param>
    /// <returns>The first match or null.</returns>
    public TagCommit? FindFirst( IEnumerable<Commit> commits )
    {
        // Build the index if not yet built.
        var index = TagCommitsBySha;
        foreach( Commit c in commits )
        {
            if( index.TryGetValue( c.Sha, out var tc ) )
            {
                return tc;
            }
        }
        return null;
    }

    /// <summary>
    /// Used by build: this checks that the <paramref name="buildCommit"/> can be built with <paramref name="version"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="buildCommit">The build commit selected by the build.</param>
    /// <param name="version">The target version. This is necessarily a "local/" prefixed version.</param>
    /// <param name="allowRebuild">True if the user allows a rebuild of an already built commit.</param>
    /// <returns>The commit build info on success, null on error.</returns>
    public CommitBuildInfo? TryGetCommitBuildInfo( IActivityMonitor monitor, Commit buildCommit, SVersion version, bool allowRebuild )
    {
        Throw.CheckArgument( version.ParsedPrefix == "local/" );
        // Preconditions for any commit.
        if( !CanBuildAnyCommit( monitor, buildCommit, version, allowRebuild, out bool isRebuild ) )
        {
            return null;
        }
        // Here isRebuild is true when the commit with the version has been found AND allowRebuild is true.
        if( isRebuild )
        {
            return new CommitBuildInfo( this, version, buildCommit, isRebuild );
        }
        // However, when allowRebuild is true, we don't want to fail here because the topology is not valid:
        // we must be able to rebuild versions in order to reach a "valid topology" state...
        // So, here, when allowRebuild is true, we blindly allow the operation.
        // => This may change in the future (with a new parameter?)...
        if( allowRebuild )
        {
            return new CommitBuildInfo( this, version, buildCommit, true );
        }

        // We are not rebuilding (the version doesn't exist).
        // Considering the existing versions, whatever the build process is, there are some invariants
        // that must be respected.
        // - There must be no gaps between major.minor.patch increments.
        // - Whatever the version is (stable, pre or post release - the ones with the -- trick), the immediate
        //   previous stable release must exist and appear in the commit parents.
        //
        // To handle exceptions, this is where the "+fake" build meta data is considered: we strictly enforce the rules
        // but a "+fake" tag on any commit circumvents the rule and de facto publicly documents the exception. 
        //
        if( _hotZone == null )
        {
            // There is no stable release at all in the ]InfVersion?,SupVersion?[ range: we allow the target version
            // to be anywhere in the range.
            return new CommitBuildInfo( this, version, buildCommit, false );
        }
        TagCommit? baseCommit = FindBaseCommitByVersion( monitor, buildCommit, version );
        if( baseCommit == null )
        {
            return null;
        }
        var div = Repo.GitRepository.Repository.ObjectDatabase.CalculateHistoryDivergence( buildCommit, baseCommit.Commit );
        if( div.CommonAncestor.Sha != baseCommit.Commit.Sha )
        {
            monitor.Error( $"""
                    Invalid Commit/Version topology in '{Repo.DisplayPath}'.

                    To build the version 'v{version}', the commit '{buildCommit.Sha}' must be a parent of commit '{baseCommit.Sha}' with version 'v{baseCommit.Version}' built on {baseCommit.Commit.Committer.When}.
                    """ );
            return null;
        }
        return new CommitBuildInfo( this, version, buildCommit, false );
    }

    bool CanBuildAnyCommit( IActivityMonitor monitor, Commit buildCommit, SVersion version, bool allowRebuild, out bool isRebuild )
    {
        isRebuild = false;
        if( !CheckNoTagConflicts( monitor ) )
        {
            return false;
        }
        if( (_supVersion != null && version >= _supVersion) || version <= _infVersion )
        {
            monitor.Error( $"""
                    Version 'v{version}' is out of the configured InfVersion="{_infVersion}" SupVersion="{_supVersion}") in '{Repo.DisplayPath}'.
                    """ );
            return false;
        }
        if( _v2C.TryGetValue( version, out var exists ) )
        {
            // If the version is found and is a +fake, then it is valid: there's nothing more to check as the
            // build commit can be the one that carries the +fake or not.
            if( exists.IsFakeVersion )
            {
                Throw.DebugAssert( "The version is the exists tag and a +fake is always stable.", version.IsStable );
                return true;
            }
            // The existing version tag must not be "+deprecated" one.
            if( !CheckDeprecatedVersion( monitor, exists ) )
            {
                return false;
            }
            // The version has already been produced. The buildCommit must be the same as the original version
            // and allowBuild must be true.
            if( exists.Commit.Sha != buildCommit.Sha )
            {
                monitor.Error( ActivityMonitor.Tags.ToBeInvestigated, $"""
                    Invalid build commit '{buildCommit.Sha}' for version 'v{version}' in '{Repo.DisplayPath}'.
                    This version has already been produced by commit '{exists.Sha}' on {exists.Commit.Committer.When}.

                    This is an error of the Build process itself: the build process must check that the version has not
                    already been released. If it's the case and a rebuild is allowed, it must consider the original build
                    commit rather than another commit (that may have the same code base).
                    """ );
                return false;
            }
            if( !allowRebuild )
            {
                // This should have been handled by the builder before calling TryGetCommitBuildInfo: this is a security.
                monitor.Error( $"""
                    The version 'v{version}' in '{Repo.DisplayPath}' already exists on the commit '{exists.Sha}' but rebuilding it is not allowed.
                    """ );
                return false;
            }
            isRebuild = true;
            return true;
        }
        // This is a new version. The build process must have checked that the build commit has not already been released with
        // a different version (otherwise we would be in the case above where the version exists).
        if( TagCommitsBySha.TryGetValue( buildCommit.Sha, out var already ) )
        {
            // If the build commit carries a +fake version, then the target version must be "roughly based" on it.
            if( already.IsFakeVersion )
            {
                if( already.Version.IsStableRoughBaseOf( version ) )
                {
                    return true;
                }
                monitor.Error( $"""
                        Invalid version 'v{version}' in '{Repo.DisplayPath}'.
                        This version is not compatible with the fake 'v{already.Version}'.
                        """ );
                return false;
            }

            // Already released under a different version.
            if( !CheckDeprecatedVersion( monitor, already ) )
            {
                return false;
            }
            // Interesting case here: the same commit must produce 2 different versions (allowing this directly
            // would require the TagCommitsBySha to be a Dictionary<string,List<TagCommit>>).
            //
            // There is only 2 cases where it makes sense to produce 2 versions from the same commit:
            // - When a CI version (reps. prerelease) has been created and, without any change in the code, a
            //   non-CI (resp. stable or "less prerelease") version must be produced.
            //   This is quite rare as it implies that no dependency updates must be made in the code: this scenario
            //   applies to "rank 0" repositories that have no dependencies to any other repositories in the stack (no
            //   upstream repositories).
            //   => This must be handled by the caller. Here we reject this case.
            //      A dedicated empty commit point must be created (with no change from its parent) to carry the "more stable" version.
            // 
            // - The "--ci.0" version that is a CI version produced from the non-CI commit is a mirror of the previous case:
            //   here also it implies that no dependency updates must be made in the code: this scenario
            //   applies to "rank 0" repositories that have no dependencies to any other repositories in the stack (no
            //   upstream repositories).
            //   However we handle this without the empty commit in order to have a true 0-based commit depth for CI builds. 
            //

            bool validCI0 = version.CINumber == 0 && version.SetCINumber( -1 ) == already.Version;
            if( !validCI0 )
            {
                monitor.Error( $"""
                        Invalid build commit '{buildCommit.Sha}' for version 'v{version}' in '{Repo.DisplayPath}'.
                        This commit has already released the version 'v{already.Version}' on {already.Commit.Committer.When}.

                        The same commit cannot produce 2 different versions.
                        """ );
                return false;
            }
        }
        return true;

        bool CheckDeprecatedVersion( IActivityMonitor monitor, TagCommit exists )
        {
            Throw.CheckArgument( !exists.IsFakeVersion );
            if( exists.IsDeprecatedVersion )
            {
                monitor.Error( $"""
                    The version '{exists.Version.ParsedText}' in '{Repo.DisplayPath}' is deprecated (on '{exists.Sha}' commit).

                    Deprecated versions should not be produced again.
                    """ );
                return false;
            }
            return true;
        }
    }

    TagCommit? FindBaseCommitByVersion( IActivityMonitor monitor, Commit buildCommit, SVersion version )
    {
        TagCommit? baseCommit = null;
        if( version.Patch == 0 )
        {
            if( version.Minor == 0 )
            {
                // New version is "Major.0.0".
                baseCommit = LastStables.FirstOrDefault( tc => tc.Version.Major < version.Major
                                                               || (tc.IsFakeVersion && tc.Version.IsStableRoughBaseOf( version )) );
                if( baseCommit == null )
                {
                    monitor.Error( $"""
                        Invalid version 'v{version}': there is no stable version 'v{version.Major - 1}.X.Y' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, 0, 0, "new \"retroactive\" major version" )}
                        """ );
                    return null;
                }
                if( !baseCommit.IsFakeVersion && baseCommit.Version.Major != version.Major - 1 )
                {
                    monitor.Error( $"""
                        Invalid version 'v{version}': the closest major is 'v{baseCommit.Version}' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, 0, 0, "gap between majors" )}
                        """ );
                    return null;
                }
            }
            else
            {
                // New version is "Major.Minor.0".
                baseCommit = LastStables.FirstOrDefault( tc => tc.Version.Major == version.Major && tc.Version.Minor < version.Minor
                                                               || (tc.IsFakeVersion && tc.Version.IsStableRoughBaseOf( version )) );
                if( baseCommit == null )
                {
                    monitor.Error( $"""
                        Invalid version 'v{version}': there is no stable version 'v{version.Major}.{version.Minor - 1}.X' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, version.Minor, 0, "new \"retroactive\" major.minor version (but this is really weird)" )}
                        """ );
                    return null;
                }
                if( !baseCommit.IsFakeVersion && baseCommit.Version.Minor != version.Minor - 1 )
                {
                    monitor.Error( $"""
                        Invalid version 'v{version}': the closest minor is 'v{baseCommit.Version}' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, version.Minor, 0, "gap between minors" )}                        
                        """ );
                    return null;
                }
            }
        }
        else
        {
            // New version is "Major.Minor.Patch".
            baseCommit = LastStables.FirstOrDefault( tc => tc.Version.Major == version.Major
                                                           && tc.Version.Minor == version.Minor
                                                           && tc.Version.Patch < version.Patch
                                                           || (tc.IsFakeVersion && tc.Version.IsStableRoughBaseOf( version )) );
            if( baseCommit == null )
            {
                monitor.Error( $"""
                        Invalid version 'v{version}': there is no stable version 'v{version.Major}.{version.Minor}.X' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, version.Minor, version.Patch, "new \"retroactive\" version (but this is really weird)" )}
                        """ );
                return null;
            }
            if( !baseCommit.IsFakeVersion && baseCommit.Version.Patch != version.Patch - 1 )
            {
                monitor.Error( $"""
                        Invalid version 'v{version}': the closest patch is 'v{baseCommit.Version}' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, version.Minor, version.Patch, "gap between patches" )}
                        """ );
                return null;
            }
        }
        return baseCommit;
    }

    static string AllowFakeMessage( Commit buildCommit,
                                    int fakeMajor,
                                    int fakeMinor,
                                    int fakePatch,
                                    string what )
    {
        return $"""
                If this is intended, you can tag the commit '{buildCommit.Sha}' (or one of its parents) with a fake version tag:
                'v{fakeMajor}.{fakeMinor}.{fakePatch}+fake'

                This will (exceptionally!) allow this {what}.
                """;
    }

    internal TagCommit AddReleaseBuildTag( SVersion version, Commit buildCommit, Tag t, BuildContentInfo contentInfo )
    {
        Throw.DebugAssert( "New version is local.", version.ParsedPrefix == "local/" );
        Throw.DebugAssert( "The 'ci.0' is on an existing TagCommit.", version.CINumber != 0 );
        Throw.DebugAssert( !_v2C.ContainsKey( version ) );
        Throw.DebugAssert( _sha2C != null );
        Throw.DebugAssert( "This must have been checked by TryGetCommitBuildInfo.",
                           !_sha2C.TryGetValue( buildCommit.Sha, out var exist ) || exist.IsFakeVersion );

        var newOne = new TagCommit( this, version, buildCommit, t, contentInfo, deprecatedInfo: null );
        _v2C.Add( version, newOne );
        _sha2C.Add( newOne.Sha, newOne );
        if( version.IsStable && !_lastStables.IsDefault )
        {
            var idx = _lastStables.BinarySearch( newOne );
            Throw.DebugAssert( idx < 0 );
            _lastStables = _lastStables.Insert( ~idx, newOne );
            _lastMajorMinorStables = default;
        }
        return newOne;
    }

    /// <summary>
    /// Removes the tag commit (not the Git tag from the repository).
    /// Handles the <see cref="AllTagCommits"/> and <see cref="TagCommitsBySha"/>.
    /// </summary>
    /// <param name="version">The version to remove.</param>
    /// <returns>The removed tag commit if has been removed.</returns>
    internal TagCommit? RemoveTagCommit( SVersion version )
    {
        if( _v2C.Remove( version, out var tc ) )
        {
            if( _sha2C != null )
            {
                _sha2C.Remove( tc.Sha );
            }
            if( version.IsStable && !_lastStables.IsDefault )
            {
                var idx = _lastStables.BinarySearch( tc );
                Throw.DebugAssert( idx >= 0 );
                _lastStables = _lastStables.RemoveAt( idx );
                _lastMajorMinorStables = default;
            }
        }
        return tc;
    }

    internal void CollectIssues( IActivityMonitor monitor, ScreenType screenType, Action<World.Issue> collector )
    {
        if( _tagConflicts != null )
        {
            foreach( var conflict in _tagConflicts.GroupBy( c => c.C ) )
            {
                switch( conflict.Key )
                {
                    case TagConflict.DuplicateInvalidTag:
                        collector( World.Issue.CreateManual( $"Found {conflict.Count()} +invalid with the same version.",
                            screenType.Text( $"""
                                        {conflict.Select( c => $" - {ToString( c.T1 )} / {ToString( c.T2 )}" ).Concatenate( Environment.NewLine )}
                                        This should be fixed manually.
                                        """ ),
                            Repo ) );
                        break;
                    case TagConflict.InvalidTagOnWrongCommit:
                        collector( World.Issue.CreateManual( $"Found {conflict.Count()} misplaced +invalid.",
                            screenType.Text( $"""
                                        {conflict.Select( c => $" - Tag {ToString( c.T1 )} invalidates the version {ToString( c.T2 )}." ).Concatenate( Environment.NewLine )}
                                        This should be fixed manually.
                                        """ ),
                            Repo ) );
                        break;
                    case TagConflict.SameVersionOnDifferentCommit:
                        collector( World.Issue.CreateManual( $"Found {conflict.Count()} same version tag on different commits.",
                            screenType.Text( $"""
                                        {conflict.Select( c => $" - {ToString( c.T1 )} / {ToString( c.T2 )}" ).Concatenate( Environment.NewLine )}
                                        This should be fixed manually.
                                        """ ),
                            Repo ) );
                        break;
                    case TagConflict.CI0VersionOnOtherCommit:
                        collector( World.Issue.CreateManual( $"Found {conflict.Count()} 'ci.0' version tag on unrelated commits.",
                            screenType.Text( $"""
                                        {conflict.Select( c => $" - {ToString( c.T1 )} is on {ToString( c.T2 )}" ).Concatenate( Environment.NewLine )}
                                        This should be fixed manually.
                                        """ ),
                            Repo ) );
                        break;
                    case TagConflict.DuplicatedVersionTag:
                        collector( World.Issue.CreateManual( $"Found {conflict.Count()} ambiguous version tags.",
                            screenType.Text( $"""
                                        {conflict.Select( c => $" - '{c.T1.V.ParsedText}' and '{c.T2.V.ParsedText}' on '{c.T1.T.Target.Sha}'." ).Concatenate( Environment.NewLine )}
                                        This should be fixed manually.
                                        """ ),
                            Repo ) );
                        break;
                }
            }
        }
        if( _removableTags.Count > 0 )
        {
            collector( new RemovableVersionTagIssue(
                                $"Found {_removableTags.Count} removable version tags.",
                                screenType.Text( $"""
                                {_removableTags.Select( t => t.FriendlyName ).Concatenate()}

                                This will be fixed by deleting them locally: a fetch from the remote will make them reappear.
                                To really remove them, the tag should be deleted from the remote origin and local +invalid
                                tags that replace them should be pushed. Use the command 'ckli tag push/pull/list/delete' to publish
                                version tags to the remote origin.
                                """ ),
                                Repo,
                                _removableTags ) );
        }
        if( _badDeprecatedTags != null )
        {
            collector( new RemovableVersionTagIssue(
                                $"Found {_badDeprecatedTags.Count} invalid +deprecated tags.",
                                screenType.Text( $"""
                                {_badDeprecatedTags.Select( t => t.T.FriendlyName ).Concatenate()}

                                Deprecated tags must be annotated tags with a content that describe the deprecation.
                                This will be fixed by deleting them locally.
                                """ ),
                                Repo,
                                [.. _badDeprecatedTags.Select( t => t.T )] ) );
        }
        if( _hotZone != null && _hotZone.HotZoneIssue != null )
        {
            collector( _hotZone.HotZoneIssue );
        }
        // Last but not least, the versioned tags that are perfectly valid BUT:
        // - Appear in the Published database with a different content
        //      => The manual issue _publishedReleaseContentIssue above.
        //      - (When the version appear in Local database and the content differ, the database is updated with the tag content.)
        // - Other issues are handled by the BuildPlugin:
        //   - A tag that doesn't appear in the Published database nor in the Local database => This is more a Debug.Assert
        //     than an issue (content are inserted or updated in the Local database when VersionInfoTag are created).
        //   - A tag that appears in the Local database but miss some of their assets in our $Local/ folder
        //     => The BuildPlugin will emit a "rebuild issue".
        //

        static string ToString( (SVersion V, Tag T) t ) => $"'{t.V.ParsedText}' on '{t.T.Target}'";
    }
}

