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
    [AllowNull] Dictionary<string, TagCommit> _sha2C;
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
                              Dictionary<string, TagCommit> sha2c,
                              List<Tag>? removableTags,
                              Dictionary<SVersion, (SVersion V, Tag T)>? invalidTags,
                              List<((SVersion V, Tag T) T1, (SVersion V, Tag T) T2, TagConflict C)>? tagConflicts,
                              List<(SVersion V, Tag T)>? badDeprecatedTags,
                              List<(SVersion V, Tag T)>? lightweightOrUnreadableRegularTags )
    {
        _hotZone = hotZone;
        _v2C = v2c;
        _sha2C = sha2c;
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
    /// No "local/" version appears if a published version exists.
    /// A "+fake" version appears in this list only if there is no corresponding published nor "local/" version.
    /// When a "local/" version exists, it exposes the potential <see cref="TagCommit.FakeVersion"/>.
    /// </para>
    /// <para>
    /// When this is empty, then <see cref="HotZone"/> is null and <see cref="HasIssue"/> is true.
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
    /// When null, a first stable version (greater than <see cref="InfVersion"/>) should be produced.
    /// This fix is handled by the Build plugin (if the root "stable" branch exists) that sets the <see cref="InfVersion"/>+fake tag on
    /// the "stable" branch's tip.
    /// This is done only if there are no <see cref="LightweightOrUnreadableRegularTags"/> (because if a tag can be successfully
    /// rebuilt, then a LastStable will exist).
    /// </para>
    /// </summary>
    public HotZoneInfo? HotZone => _hotZone;

    /// <summary>
    /// Gets a <see cref="TagCommit"/> for a version.
    /// </summary>
    /// <param name="version">The version to find.</param>
    /// <returns>The tag commit if it exists, null otherwise.</returns>
    public TagCommit? GetTagCommit( SVersion version ) => _v2C.GetValueOrDefault( version );

    /// <summary>
    /// Gets a <see cref="TagCommit"/> for a version.
    /// <para>
    /// For "ci.0" version (when <see cref="SVersion.CINumber"/> is 0, this locates the commit of the base version.
    /// The found base may have a <see cref="TagCommit.CI0Version"/>.
    /// </para>
    /// </summary>
    /// <param name="version">The version to find.</param>
    /// <param name="tagCommit">On success, the found tag commit.</param>
    /// <returns>True in success, false if the version doesn't exist.</returns>
    public bool TryGetTagCommit( SVersion version, [NotNullWhen( true )] out TagCommit? tagCommit ) => (tagCommit = GetTagCommit( version )) != null;

    /// <summary>
    /// Gets the versioned tag commit indexed by their <see cref="TagCommit.Sha"/>: at most one
    /// <see cref="TagCommit"/> per commit.
    /// <para>
    /// This index is built once by the <see cref="VersionTagPlugin"/>. When a commit carries both a "+fake"
    /// and a regular or "+deprecated" version tag, the entry is the non-fake one and the "+fake" is exposed
    /// by its <see cref="TagCommit.FakeVersion"/>. Any other commit bearing more than one version is a
    /// <see cref="TagConflict.MultipleVersionsOnSameCommit"/> conflict: <see cref="HasIssue"/> is then true.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, TagCommit> TagCommitsBySha => _sha2C;

    /// <summary>
    /// Gets all the <see cref="TagCommit"/>.
    /// </summary>
    public IEnumerable<TagCommit> AllTagCommits => _v2C.Values;

    /// <summary>
    /// Enumerates all the versions with the <see cref="Tag"/> that declares them and their associated <see cref="TagCommit"/>.
    /// <list type="bullet">
    ///     <item>
    ///     The <see cref="TagCommit.Version"/>, <see cref="TagCommit.Version"/> and the TagCommit itself for each item
    ///     of <see cref="AllTagCommits"/>.
    ///     </item>
    ///     <item>The <see cref="TagCommit.CI0Version"/> if it is not null.</item>
    ///     <item>
    ///     The version and tag of the <see cref="TagCommit.FakeVersion"/> if it is not null.
    ///     This FakeVersion (only appears on true <see cref="TagCommit.IsBuildingOrLocal"/>) doesn't appear in the <see cref="AllTagCommits"/>.
    ///     </item>
    /// </list>
    /// </summary>
    public IEnumerable<(SVersion Version, Tag Tag, TagCommit Commit)> AllVersions
    {
        get
        {
            foreach( var tc in _v2C.Values )
            {
                yield return (tc.Version, tc.Tag, tc);
                if( tc.CI0Version != null ) yield return (tc.CI0Version, tc.CI0VersionTag!, tc);
                if( tc.FakeVersion != null ) yield return (tc.FakeVersion.Version, tc.FakeVersion.Tag, tc.FakeVersion);
            }
        }
    }

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
        var index = _sha2C;
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
    /// Destroys all "local/" or "building/" releases for which <paramref name="filter"/> returns true.
    /// Note that <see cref="RemovableTags"/> if any are considered but <see cref="HasTagConflicts"/> must be false
    /// otherwise an <see cref="InvalidOperationException"/> is throw.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="filter">Optional filter.</param>
    /// <param name="removeFromNuGetGlobalCache">
    /// False to let the packages in the NuGet global cache (if they exists).
    /// The global cache is "%userprofile%\.nuget\packages" on windows and "~/.nuget/packages" on Mac/Linux.
    /// </param>
    /// <returns>
    /// True on success, false on error (one of the assets cannot be properly deleted).
    /// </returns>
    public bool DestroyLocalReleases( IActivityMonitor monitor, Func<SVersion, bool>? filter, bool removeFromNuGetGlobalCache = true )
    {
        Throw.CheckState( !HasTagConflicts );
        bool success = true;
        var cleanupLocals = new List<SVersion>();
        foreach( var t in _removableTags )
        {
            // It is necessarily a valid SVersion with a valid prefix (otherwise it would have been
            // collected in invalidParsedPrefixTags, nonConformantTags or invalidTags).
            var tagName = t.FriendlyName;
            if( tagName.StartsWith( "local/", StringComparison.Ordinal ) || tagName.StartsWith( "building/", StringComparison.Ordinal ) )
            {
                var v = SVersion.Parse( tagName, allowPrefix: true );
                if( filter == null || filter( v ) )
                {
                    cleanupLocals.Add( SVersion.Parse( tagName, allowPrefix: true ) );
                }
            }
        }
        cleanupLocals.AddRange( AllVersions.Select( tc => tc.Version )
                                           .Where( v => v.IsBuildingOrLocal() && v.BuildMetaData.Length == 0 && (filter == null || filter( v )) ) );
        if( cleanupLocals.Count > 0 )
        {
            using( monitor.OpenInfo( $"""
                Destroying {cleanupLocals.Count} "local/" versions in '{Repo.DisplayPath}':
                {cleanupLocals.Select( v => v.ParsedText ).Concatenate()}.
                """ ) )
            {
                foreach( var local in cleanupLocals )
                {
                    success &= DestroyLocalRelease( monitor, local, removeFromNuGetGlobalCache );
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Destroys a "building/" or "local/" released version. The version tag is deleted, any artifacts are removed.
    /// <para>
    /// This is idempotent (if the "building/" or "local/" version tag doesn't exist, nothing is done).
    /// </para>
    /// The <paramref name="version"/> can appear in the <see cref="RemovableTags"/> but <see cref="HasTagConflicts"/> must
    /// be false otherwise an <see cref="InvalidOperationException"/> is throw.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="version">
    /// The "building/" or "local/" release to destroy.
    /// <see cref="SVersion.IsCSVersion"/> must be true and <see cref="SVersion.BuildMetaData"/> must be empty.
    /// </param>
    /// <param name="removeFromNuGetGlobalCache">
    /// False to let the package in the NuGet global cache (if it exists).
    /// The global cache is "%userprofile%\.nuget\packages" on windows and "~/.nuget/packages" on Mac/Linux.
    /// </param>
    /// <returns>
    /// True on success, false on error: the <paramref name="version"/> is a +fake or +deprecated one, or
    /// the assets cannot be properly deleted.
    /// </returns>
    public bool DestroyLocalRelease( IActivityMonitor monitor, SVersion version, bool removeFromNuGetGlobalCache = true )
    {
        Throw.CheckState( !HasTagConflicts );

        // Usual case: the version maps to a TagCommit.
        if( !TryGetFromTagCommit( monitor, _v2C, version, out var tag, out var v, out var tagCommit ) )
        {
            return false;
        }
        if( tagCommit == null )
        {
            // Not found: there is no TagCommit for the version. It can be a removable tag: this is not an optimized
            // path but we don't care.
            (tag, v) = _removableTags.Select( t => (T: t, V: SVersion.Parse( t.FriendlyName, allowPrefix: true )) )
                                     .FirstOrDefault( tv => tv.V == version );
            if( tag == null )
            {
                monitor.Info( $"Version tag 'building/v{version}' or 'local/v{version}' not found. Skipped DestroyLocalRelease." );
                return true;
            }
        }
        Throw.DebugAssert( "We have a Tag and its Version (and may be a TagCommit).", tag != null && v != null );
        if( !v.IsBuildingOrLocal() )
        {
            monitor.Warn( $"Existing versioned tag '{tag.FriendlyName}' is not 'building/' or 'local/'. Skipped DestroyLocalRelease." );
            return true;
        }
        if( v.HasFakeMetadata || v.HasDeprecatedMetadata )
        {
            monitor.Error( $"DestroyLocalRelease failed: tag '{tag.FriendlyName}' must not be +fake or +deprecated." );
            return false;
        }
        // Extracts the content.
        BuildContentInfo? tagContent;
        if( tagCommit != null )
        {
            tagContent = tagCommit.BuildContentInfo;
            Throw.DebugAssert( """
                If we are on the ci.0, then we have the BuildContentInfo of the ci.0.
                If we are on the TagCommit, then it is not Fake (filtered above), so we have a content.
                """, tagContent != null );
        }
        else
        {
            if( !BuildContentInfo.TryParse( tag.Annotation?.Message, out tagContent ) )
            {
                if( tag.Annotation == null )
                {
                    monitor.Error( $"Existing removable versioned tag '{tag.FriendlyName}' is a lightweight tag. Skipped DestroyLocalRelease." );
                }
                else
                {
                    monitor.Error( $"Existing removable versioned tag '{tag.FriendlyName}' has an invalid content. Skipped DestroyLocalRelease." );
                }
                return false;
            }
        }

        if( tagCommit != null ) RemoveTagCommit( monitor, version );
        return _versionTagPlugin.DoDestroyLocalRelease( monitor, Repo, tag, version, tagContent, removeFromNuGetGlobalCache );

        static bool TryGetFromTagCommit( IActivityMonitor monitor,
                                         Dictionary<SVersion, TagCommit> v2C,
                                         SVersion requestedVersion,
                                         out Tag? tag,
                                         out SVersion? v,
                                         out TagCommit? tagCommit )
        {
            tag = null;
            v = null;
            if( !v2C.TryGetValue( requestedVersion, out tagCommit ) )
            {
                return true;
            }
            // Use the right (tag,version).
            if( requestedVersion == tagCommit.CI0Version )
            {
                Throw.DebugAssert( tagCommit.CI0VersionTag != null );
                tag = tagCommit.CI0VersionTag;
                v = tagCommit.CI0Version;
            }
            else
            {
                tag = tagCommit.Tag;
                v = tagCommit.Version;
            }
            return true;
        }
    }


    /// <summary>
    /// Used by build: this checks that the <paramref name="buildCommit"/> can be built with <paramref name="version"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="buildCommit">The build commit selected by the build.</param>
    /// <param name="version">The target version. This is necessarily a "local/" prefixed version.</param>
    /// <param name="allowRebuildVersion">
    /// True to allow the target <paramref name="version"/> to already exist on another commit.
    /// This is the "force rebuild" of the Build plugin.
    /// </param>
    /// <returns>The commit build info on success, null on error.</returns>
    public CommitBuildInfo? TryGetCommitBuildInfo( IActivityMonitor monitor, Commit buildCommit, SVersion version, bool allowRebuildVersion )
    {
        // Preconditions for any commit.
        if( !CanBuildAnyCommit( monitor, buildCommit, version, allowRebuildVersion ) )
        {
            return null;
        }
        // Considering the existing versions, whatever the build process is, there are some invariants
        // that must be respected.
        // - There must be no gaps between major.minor.patch increments.
        // - Whatever the version is (stable, pre or post release - the ones with the -- trick), the immediate
        //   previous stable release must exist and appear in the commit parents.
        //
        // To handle exceptions, this is where the "+fake" build meta data is considered: we strictly enforce the rules
        // but a "+fake" tag on any commit circumvents the rule and de facto publicly documents the exception.
        //
        // When there is no stable release at all in the ]InfVersion?,SupVersion?[ range: we allow the target
        // version to be anywhere in the range.
        if( _hotZone != null )
        {
            TagCommit? baseCommit = FindBaseCommitByVersion( monitor, buildCommit, version );
            if( baseCommit == null )
            {
                return null;
            }
            var div = Repo.GitRepository.Repository.ObjectDatabase.CalculateHistoryDivergence( baseCommit.Commit, buildCommit );
            if( div.AheadBy is not 0 )
            {
                monitor.Error( $"""
                Invalid Commit/Version topology in '{Repo.DisplayPath}'.

                To build the version 'v{version}', the commit '{buildCommit.Sha}' must be a parent of commit '{baseCommit.Sha}' with version 'v{baseCommit.Version}' built on {baseCommit.Commit.Committer.When}.
                """ );
                return null;
            }
        }
        return new CommitBuildInfo( this, version, buildCommit );
    }

    bool CanBuildAnyCommit( IActivityMonitor monitor, Commit buildCommit, SVersion version, bool allowRebuildVersion )
    {
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
            if( !exists.CheckDeprecatedVersion( monitor ) )
            {
                return false;
            }
            // The version has already been produced. The buildCommit must be the same as the original version
            // unless the caller explicitly allows the version to move to another commit.
            if( exists.Commit.Sha != buildCommit.Sha && !allowRebuildVersion )
            {
                monitor.Error( ActivityMonitor.Tags.ToBeInvestigated, $"""
                    Invalid build commit '{buildCommit.Sha}' for version 'v{version}' in '{Repo.DisplayPath}'.
                    This version has already been produced by commit '{exists.Sha}' on {exists.Commit.Committer.When}.

                    This is an error of the Build process itself: allowRebuildVersion must be explicitly set.
                    """ );
                return false;
            }
            return true;
        }
        // This is a new version. The build process must have checked that the build commit is not already associated to
        // an incompatible version.
        if( _sha2C.TryGetValue( buildCommit.Sha, out var already ) )
        {
            var msg = already.CanBearVersion( version );
            if( msg != null )
            {
                monitor.Error( msg );
                return false;
            }
        }
        return true;

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
                                                               || (tc.IsOrHasFakeVersion && tc.Version.IsStableRoughBaseOf( version )) );
                if( baseCommit == null )
                {
                    monitor.Error( $"""
                        Invalid version 'v{version}': there is no stable version 'v{version.Major - 1}.X.Y' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, 0, 0, "new \"retroactive\" major version" )}
                        """ );
                    return null;
                }
                if( !baseCommit.IsOrHasFakeVersion && baseCommit.Version.Major != version.Major - 1 )
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
                                                               || (tc.IsOrHasFakeVersion && tc.Version.IsStableRoughBaseOf( version )) );
                if( baseCommit == null )
                {
                    monitor.Error( $"""
                        Invalid version 'v{version}': there is no stable version 'v{version.Major}.{version.Minor - 1}.X' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, version.Minor, 0, "new \"retroactive\" major.minor version (but this is really weird)" )}
                        """ );
                    return null;
                }
                if( !baseCommit.IsOrHasFakeVersion && baseCommit.Version.Minor != version.Minor - 1 )
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
                                                           || (tc.IsOrHasFakeVersion && tc.Version.IsStableRoughBaseOf( version )) );
            if( baseCommit == null )
            {
                monitor.Error( $"""
                        Invalid version 'v{version}': there is no stable version 'v{version.Major}.{version.Minor}.X' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, version.Minor, version.Patch, "new \"retroactive\" version (but this is really weird)" )}
                        """ );
                return null;
            }
            if( !baseCommit.IsOrHasFakeVersion && baseCommit.Version.Patch != version.Patch - 1 )
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
        // We may be here on a "local/" version or not. Nominal case is "local/" (we are building through the Roadmap
        // or the FixWorkflow) but when rebuilding LightweightOrUnreadableRegularTags, there's no TagCommit for
        // the bad tag. 
        Throw.DebugAssert( "The 'ci.0' is on an existing TagCommit.", version.CINumber != 0 );
        Throw.DebugAssert( !_v2C.ContainsKey( version ) );
        Throw.DebugAssert( "This must have been checked by TryGetCommitBuildInfo.",
                           !_sha2C.TryGetValue( buildCommit.Sha, out var exist ) || exist.IsFakeVersion );

        var newOne = new TagCommit( this, version, buildCommit, t, contentInfo, deprecatedInfo: null );
        _v2C.Add( version, newOne );
        // The commit may already carry a "+fake" (this is how a version gap is allowed): the new TagCommit
        // becomes the "by sha" entry and the fake is attached to it.
        if( _sha2C.TryGetValue( newOne.Sha, out var fake ) )
        {
            Throw.DebugAssert( fake.IsFakeVersion );
            newOne.SetFake( fake );
        }
        _sha2C[newOne.Sha] = newOne;
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
    /// <para>
    /// If the removed version is the <see cref="HotZoneInfo.LastStable"/>, the <see cref="HotZone"/> is set to null.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version to remove.</param>
    /// <returns>The removed tag commit if it has been removed.</returns>
    internal TagCommit? RemoveTagCommit( IActivityMonitor monitor, SVersion version )
    {
        if( _v2C.Remove( version, out var tc ) )
        {
            if( version == tc.CI0Version )
            {
                tc.ClearCI0VersionTag();
            }
            else
            {
                // The "+fake" tag on the commit is not removed: it becomes the "by sha" entry again.
                var fake = tc.FakeVersion;
                if( fake != null )
                {
                    _sha2C[tc.Sha] = fake;
                }
                else
                {
                    _sha2C.Remove( tc.Sha );
                }
                if( version.IsStable && !_lastStables.IsDefault )
                {
                    var idx = _lastStables.BinarySearch( tc );
                    Throw.DebugAssert( idx >= 0 );
                    _lastStables = _lastStables.RemoveAt( idx );
                    _lastMajorMinorStables = default;
                    if( _hotZone != null && !_hotZone.OnTagCommitRemoved( tc ) )
                    {
                        monitor.Info( $"Removed {tc} that is the current LastStable in '{Repo.DisplayPath}': the HotZone has been disabled." );
                        _hotZone = null;
                    }
                }
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
                    case TagConflict.MultipleVersionsOnSameCommit:
                        collector( World.Issue.CreateManual( $"Found {conflict.Count()} commits bearing more than one version.",
                            screenType.Text( $"""
                                        {conflict.Select( c => $" - {ToString( c.T1 )} and {ToString( c.T2 )}" ).Concatenate( Environment.NewLine )}
                                        A commit must release at most one version (a '+fake' tag on an already versioned commit is the only exception).
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

