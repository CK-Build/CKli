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
    readonly SVersion _version;
    readonly Commit _commit;
    readonly string _sha;
    Tag _tag;
    Tag? _ci0Tag;
    string? _message;
    BuildContentInfo? _buildContentInfo;
    DeprecatedTagInfo? _deprecatedInfo;

    internal TagCommit( VersionTagInfo repoInfo, SVersion version, Commit commit, Tag tag, BuildContentInfo? contentInfo, DeprecatedTagInfo? deprecatedInfo )
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
    /// Gets the tag's message if this <see cref="Tag"/> is an Annotated tag. Null otherwise.
    /// </summary>
    public string? TagMessage => _message ??= _tag.Annotation?.Message;

    /// <summary>
    /// Gets the build content info if <see cref="IsFakeVersion"/> is false. Null otherwise.
    /// </summary>
    public BuildContentInfo? BuildContentInfo => _buildContentInfo;

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

    internal void SetCI0VersionTag( Tag tag )
    {
        Throw.DebugAssert( tag != null && tag.IsAnnotated && BuildContentInfo.TryParse( tag.Annotation.Message, out _ ) );
        Throw.DebugAssert( SVersion.Parse( tag.FriendlyName, allowPrefix:true, mustBeCSVersion: true ).CINumber == 0
                           && SVersion.Parse( tag.FriendlyName, allowPrefix: true, mustBeCSVersion: true ).SetCINumber( -1 ) == _version );
        _ci0Tag = tag;
    }

    internal void UpdateVersionTag( Tag t )
    {
        Throw.DebugAssert( t.IsAnnotated );
        _message = t.Annotation.Message;
        _tag = t;
        _buildContentInfo = null;
    }
}
