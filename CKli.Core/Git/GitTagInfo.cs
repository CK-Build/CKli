using LibGit2Sharp;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Principal;

namespace CKli.Core;

/// <summary>
/// Captures <see cref="TagInfo"/> and <see cref="TagInfo.Group"/> for a repository.
/// </summary>
public sealed partial class GitTagInfo
{
    readonly ImmutableArray<TagInfo> _tags;
    readonly ImmutableArray<TagInfo> _invalidTags;
    Dictionary<string, TagInfo>? _indexedTags;
    ImmutableArray<TagInfo.Group> _groups;
    int _fetchRequiredCount;

    public GitTagInfo( ImmutableArray<TagInfo>.Builder result, ImmutableArray<TagInfo>.Builder? invalidTags, int fetchRequiredCount )
    {
        _tags = result.DrainToImmutable();
        _invalidTags = invalidTags != null ? invalidTags.DrainToImmutable() : [];
        _fetchRequiredCount = fetchRequiredCount;
    }

    /// <summary>
    /// Git reference names (like branches or tags) case sensitivity is a nightmare: it depends on the file system
    /// but not only: even on a case insensitive filesystem, your references may end up "packed", in a
    /// file specifying on reference per line. In this case, your references are always case sensitive,
    /// regardless of your filesystem.
    /// <para>
    /// CKli considers that tag names MUST only be ascii and letters must be in lower case. Tags that are not compliant with this
    /// strict rule are simply ignored. The static <see cref="GitRepository.IsCKliValidTagName(System.ReadOnlySpan{char})"/>
    /// implements the check.
    /// </para>
    /// </summary>
    public ImmutableArray<TagInfo> InvalidTags => _invalidTags;

    /// <summary>
    /// Gets the number of tags that have no local target commit. See <see cref="TagInfo.Commit"/>.
    /// <para>
    /// This is always 0 when this <see cref="GitTagInfo"/> has been obtained by <see cref="GitRepository.GetLocalTags(CK.Core.IActivityMonitor, out GitTagInfo?)"/>.
    /// </para>
    /// </summary>
    public int FetchRequiredCount => _fetchRequiredCount;

    /// <summary>
    /// Gets all the tags ordered by <see cref="TagInfo.CommitDateUtc"/> and then by <see cref="TagInfo.CanonicalName"/>.
    /// <para>
    /// When obtained by <see cref="GitRepository.GetRemoteTags(CK.Core.IActivityMonitor, out GitTagInfo?, string)"/>,
    /// this list can start with "fetch required" tags that have a null <see cref="TagInfo.Commit"/>.
    /// </para>
    /// </summary>
    public ImmutableArray<TagInfo> Tags => _tags;

    /// <summary>
    /// Gets the <see cref="Tags"/> indexed by their <see cref="TagInfo.CanonicalName"/> (starts with "refs/tags/").
    /// </summary>
    public IReadOnlyDictionary<string, TagInfo> IndexedTags => _indexedTags ??= _tags.ToDictionary( t => t.CanonicalName );

    /// <summary>
    /// Gets the tags grouped by <see cref="TagInfo.CommitDateUtc"/> and then by <see cref="TagInfo.CanonicalName"/>.
    /// </summary>
    public ImmutableArray<TagInfo.Group> GroupedTags => _groups.IsDefault
                                                            ? (_groups = TagInfo.GetGroups( _tags, out _fetchRequiredCount ))
                                                            : _groups;

}
