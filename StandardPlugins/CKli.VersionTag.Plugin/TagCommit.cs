using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Diagnostics.CodeAnalysis;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Captures a <see cref="Commit"/> and its version <see cref="Tag"/>.
/// <para>
/// This is comparable but reverts the <see cref="SVersion.CompareTo(SVersion?)"/> order.
/// </para>
/// </summary>
public sealed class TagCommit : IComparable<TagCommit>, IEquatable<TagCommit>, BranchModel.Plugin.ITagCommit
{
    readonly VersionTagInfo _versionRepoInfo;
    readonly Commit _commit;
    readonly string _sha;
    SVersion _version;
    Tag _tag;
    Tag? _ci0Tag;
    SVersion? _ci0Version;
    string? _message;
    BuildContentInfo? _buildContentInfo;
    DeprecatedTagInfo? _deprecatedInfo;
    TagCommit? _fakeVersion;

    internal TagCommit( VersionTagInfo repoInfo,
                        SVersion version,
                        Commit commit,
                        Tag tag,
                        BuildContentInfo? contentInfo,
                        DeprecatedTagInfo? deprecatedInfo )
    {
        Throw.DebugAssert( "Only fake version can have no content info.", version.HasFakeMetadata == (contentInfo == null) );
        _versionRepoInfo = repoInfo;
        _version = version;
        _commit = commit;
        _tag = tag;
        _buildContentInfo = contentInfo;
        _deprecatedInfo = deprecatedInfo;
        _sha = commit.Sha;
    }

    internal void SetFake( TagCommit fake )
    {
        Throw.DebugAssert( fake.IsFakeVersion && fake.Version == _version && IsBuildingOrLocal && _fakeVersion == null );
        _fakeVersion = fake;
    }

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _versionRepoInfo.Repo;

    /// <summary>
    /// Gets the version.
    /// </summary>
    public SVersion Version => _version;

    /// <summary>
    /// Gets the commit.
    /// </summary>
    public Commit Commit => _commit;

    /// <summary>
    /// Gets this commit sha.
    /// </summary>
    public string Sha => _sha;

    /// <summary>
    /// Gets whether this version tag is "+fake" one: it is here only
    /// to enables gaps between versions that would otherwise be rejected.
    /// </summary>
    [MemberNotNullWhen( false, nameof( BuildContentInfo ) )]
    public bool IsFakeVersion => _version.HasFakeMetadata;

    /// <summary>
    /// Gets whether <see cref="IsFakeVersion"/> is true or an associated <see cref="FakeVersion"/> exists.
    /// </summary>
    public bool IsOrHasFakeVersion => _version.HasFakeMetadata || _fakeVersion != null;

    /// <summary>
    /// Gets whether this version is a "building" or "local/" one.
    /// </summary>
    public bool IsBuildingOrLocal => _version.IsBuildingOrLocal();

    /// <summary>
    /// Gets whether this version tag is "+deprecated" one. <see cref="DeprecatedInfo"/> is necessarily not null.
    /// </summary>
    [MemberNotNullWhen( true, nameof( DeprecatedInfo ) )]
    public bool IsDeprecatedVersion => _deprecatedInfo != null;

    /// <summary>
    /// Gets the deprecated info parsed from <see cref="TagMessage"/> if <see cref="IsDeprecatedVersion"/> is true.
    /// </summary>
    public DeprecatedTagInfo? DeprecatedInfo => _deprecatedInfo;

    /// <summary>
    /// Gets whether this version tag is not a "+fake" nor a "+deprecated" one.
    /// </summary>
    [MemberNotNullWhen( true, nameof( BuildContentInfo ) )]
    public bool IsRegularVersion => !IsDeprecatedVersion && !IsFakeVersion;

    /// <summary>
    /// The tag object.
    /// </summary>
    public Tag Tag => _tag;

    /// <summary>
    /// Gets the "--ci.0" or ".ci.0" tag if one exists for this TagCommit. If it exists, it is necessarily
    /// the <see cref="SVersion.CINumber"/> = 0 for this <see cref="Version"/> (and this version is not itself a CI version).
    /// <para>
    /// Note that, by design, this <see cref="BuildContentInfo"/> applies to the ci.0 tag.
    /// </para>
    /// </summary>
    public Tag? CI0VersionTag => _ci0Tag;

    /// <summary>
    /// Gets the the "--ci.0" or ".ci.0" parsed <see cref="CI0VersionTag"/>.
    /// </summary>
    public SVersion? CI0Version => _ci0Version;

    /// <summary>
    /// Gets the +fake commit for this <see cref="Version"/> if it exists.
    /// This is available only when <see cref="IsBuildingOrLocal"/> is true.
    /// </summary>
    public TagCommit? FakeVersion => _fakeVersion;

    /// <summary>
    /// Gets the tag's message if this <see cref="Tag"/> is an Annotated tag. Null otherwise.
    /// </summary>
    public string? TagMessage => _message ??= _tag.Annotation?.Message;

    /// <summary>
    /// Gets the build content info. Null if <see cref="IsFakeVersion"/> is true and <see cref="CI0Version"/> is null.
    /// </summary>
    public BuildContentInfo? BuildContentInfo => _buildContentInfo;

    /// <summary>
    /// Tests whether a new version can be associated to this commit.
    /// This prevents 2 incompatible versions to be carried by the same commit. 
    /// </summary>
    /// <param name="version">The new version.</param>
    /// <returns>The error message or null.</returns>
    public string? CanBearVersion( SVersion version )
    {
        // If this is a +fake version, then the new version must be "roughly based" on it.
        if( IsFakeVersion )
        {
            if( Version.IsStableRoughBaseOf( version ) )
            {
                return null;
            }
            return $"""
                    Invalid version 'v{version}' in '{Repo.DisplayPath}'.
                    This version is not compatible with the fake 'v{Version}'.
                    """;
        }

        // If this Version is +deprecated, we refuse to generate any version from it.
        var deprecated = CheckDeprecatedVersion();
        if( deprecated != null ) return deprecated;

        // Interesting case here: the same commit must produce 2 different versions (allowing this directly
        // would require the TagCommitsBySha to be a Dictionary<string,List<TagCommit>>).
        //
        // There is only 2 cases where it makes sense to produce 2 versions from the same commit:
        // - When a CI version (resp. prerelease) has been created and, without any change in the code, a
        //   non-CI (resp. stable or "less prerelease") version must be produced.
        //   This is quite rare as it implies that no dependency updates must be made in the code: this scenario
        //   applies to "rank 0" repositories that have no dependencies to any other repositories in the stack (no
        //   upstream repositories).
        //   => This must be handled by the caller. Here we reject this case.
        //      A dedicated empty commit point must be created (with no change from its parent) to carry the "more stable" version
        //      or, if it is a "local/" version, it could be "DestroyLocalRelease" before the build.
        // 
        // - The "--ci.0" version that is a CI version produced from the non-CI commit is a mirror of the previous case:
        //   here also it implies that no dependency updates must be made in the code: this scenario
        //   applies to "rank 0" repositories that have no dependencies to any other repositories in the stack (no
        //   upstream repositories).
        //   However we handle this without the empty commit in order to have a true 0-based commit depth for CI builds. 
        //

        // Then, there is the "rolling local build" case: if this version is a local one with the same branch name as the
        // new one, then it's fine: this previous version will be destroyed (ApplyReleaseBuildTag calls DestroyLocalReleases).
        bool rollingLocal = IsBuildingOrLocal && Version.BranchName == version.BranchName;

        Throw.DebugAssert( "The IsFakeVersion case has been handled above.", !IsOrHasFakeVersion || FakeVersion != null );
        Throw.DebugAssert( "(HasFakeVersion => IsLocal) <=> (!IsLocal => !HasFakeVersion)", FakeVersion == null || IsBuildingOrLocal );
        // The --ci.0 case implies that the versions have the same branch name (the rolling local build above generalizes it),
        // but here we save the case where this Version is published.
        bool validCI0 = !IsBuildingOrLocal
                        && ((version.CINumber == 0 && version.SetCINumber( -1, impactStablePatchNumber: true ) == Version)
                            || Version.CINumber == 0 && Version.SetCINumber( -1, impactStablePatchNumber: true ) == version);

        if( !rollingLocal && !validCI0 )
        {
            return $"""
                    Invalid build commit '{Sha.AsSpan( 0, 7 )} {Commit.MessageShort}' for version 'v{version}' in '{Repo.DisplayPath}'.
                    This commit has already released the version 'v{Version}' on {Commit.Committer.When}.

                    The same commit cannot produce 2 different versions.
                    """;
        }
        return null;
    }


    /// <summary>
    /// Applies <see cref="BuildResult.CommitBuilding(Repo, Tag, SVersion)"/> on (<see cref="CI0VersionTag"/>,<see cref="CI0Version"/>)
    /// or (<see cref="Tag"/>,<see cref="Version"/>).
    /// </summary>
    /// <param name="ci0Tag">Whether the ci.0 version is concerned.</param>
    /// <returns>The updated tag in the repository and the version.</returns>
    public (Tag,SVersion) CommitBuilding( bool ci0Tag )
    {
        if( ci0Tag )
        {
            Throw.CheckState( _ci0Tag != null && _ci0Version != null );
            return (_ci0Tag, _ci0Version) = BuildResult.CommitBuilding( _versionRepoInfo.Repo, _ci0Tag, _ci0Version );
        }
        return (_tag, _version) = BuildResult.CommitBuilding( _versionRepoInfo.Repo, _tag, _version );
    }

    /// <summary>
    /// TagCommits are first ordered by <see cref="Repo.Index"/> and then reverts the <see cref="SVersion.CompareTo(SVersion?)"/> result:
    /// we want the first <see cref="TagCommit"/> in <see cref="VersionTagInfo.LastStables"/> to be the latest one, not the oldest one.
    /// </summary>
    /// <param name="other">The other versioned tag commit.</param>
    /// <returns>The standard compare result.</returns>
    public int CompareTo( TagCommit? other )
    {
        if( other is null ) return 1;
        int cmp = Repo.Index.CompareTo( other.Repo.Index );
        return cmp != 0 ? cmp : -_version.CompareTo( other?._version );
    }

    /// <inheritdoc />
    public bool Equals( TagCommit? other ) => _versionRepoInfo == other?._versionRepoInfo && _version.Equals( other?._version );

    /// <inheritdoc />
    public override bool Equals( object? obj ) => Equals( obj as TagCommit );

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine( _versionRepoInfo, _version );

    /// <summary>
    /// Overridden to return the tag and referenced commit's sha.
    /// </summary>
    /// <returns></returns>
    public override string ToString() => $"Tag '{Repo.DisplayPath}/{_version.ParsedText}' references Commit '{_sha}'";

    internal bool CheckDeprecatedVersion( IActivityMonitor monitor )
    {
        var msg = CheckDeprecatedVersion();
        if( msg != null )
        {
            monitor.Error( msg );
            return false;
        }
        return true;
    }

    internal string? CheckDeprecatedVersion()
    {
        Throw.DebugAssert( !IsFakeVersion );
        return IsDeprecatedVersion
                ? $"""
                        The version '{Version.ParsedText}' in '{Repo.DisplayPath}' is deprecated (on '{Sha.AsSpan(0,7)} {Commit.MessageShort}' commit).

                        Deprecated versions should not be produced again.
                        """
                : null;
    }

    internal void ClearCI0VersionTag()
    {
        Throw.DebugAssert( _ci0Tag != null && _ci0Version != null );
        _ci0Version = null;
        _ci0Tag = null;
    }

    internal void SetCI0VersionTag( Tag tag, SVersion v )
    {
#if DEBUG
        var vParsed = SVersion.Parse( tag.FriendlyName, allowPrefix: true, mustBeCSVersion: true );
        Throw.DebugAssert( v == vParsed );
        Throw.DebugAssert( v.IsBuildingOrLocal() == vParsed.IsBuildingOrLocal() );
#endif
        Throw.DebugAssert( tag != null && tag.IsAnnotated && BuildContentInfo.TryParse( tag.Annotation.Message, out _ ) );
        Throw.DebugAssert( v.CINumber == 0
                            && ((v.SetCINumber( -1, impactStablePatchNumber: false ) == _version && IsOrHasFakeVersion)
                                ||
                                (v.SetCINumber( -1, impactStablePatchNumber: true ) == _version && !IsOrHasFakeVersion)) );
        _ci0Tag = tag;
        _ci0Version = v;
        // If we have no content info (because we are a +fake), then we acquire the content info from the
        // --ci.0 tag.
        if( _buildContentInfo == null )
        {
            // This necessarily succeeds (DebugAssert above).
            _ = BuildContentInfo.TryParse( tag.Annotation.Message, out _buildContentInfo );
        }
    }

    internal void UpdateVersionTag( Tag t )
    {
        Throw.DebugAssert( t.IsAnnotated );
        _message = t.Annotation.Message;
        _tag = t;
        _buildContentInfo = null;
    }
}
