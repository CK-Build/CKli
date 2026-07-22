using LibGit2Sharp;
using System.Collections.Generic;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Captures the all the reachable <see cref="TagCommit"/> from the <see cref="Tip"/> up to <see cref="LastStable"/>
/// with their 0-based increasing level from the first tagged commit found.
/// </summary>
public sealed class TagCommitTree
{
    readonly Commit _tip;
    readonly List<(TagCommit, int)> _content;
    readonly VersionTagInfo.HotZoneInfo _hotZone;

    internal TagCommitTree( VersionTagInfo.HotZoneInfo hotZone, Commit tip, List<(TagCommit, int)> content )
    {
        _hotZone = hotZone;
        _tip = tip;
        _content = content;
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
}
