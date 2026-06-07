using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CKli.Core;

/// <summary>
/// Captures <see cref="TagInfo"/> and <see cref="TagInfo.Group"/> for a repository.
/// </summary>
public sealed partial class GitTagInfo
{
    readonly ImmutableArray<TagInfo> _tags;
    readonly ImmutableArray<string> _invalidTags;
    Dictionary<string, TagInfo>? _indexedTags;
    ImmutableArray<TagInfo.Group> _groups;

    internal GitTagInfo( ImmutableArray<TagInfo>.Builder result, ImmutableArray<string>.Builder? invalidTags )
    {
        _tags = result.DrainToImmutable();
        _invalidTags = invalidTags != null ? invalidTags.DrainToImmutable() : [];
    }

    /// <summary>
    /// Git reference names (like branches or tags) case sensitivity is a nightmare: it depends on the file system
    /// but not only: even on a case insensitive filesystem, your references may end up "packed", in a
    /// file specifying on reference per line. In this case, your references are always case sensitive,
    /// regardless of your filesystem.
    /// <para>
    /// CKli considers that tag names MUST only be ascii and letters must be in lower case. Tags that are not compliant with this
    /// strict rule are simply ignored. The static <see cref="GitRepository.IsCKliValidTagName(ReadOnlySpan{char})"/>
    /// implements the check.
    /// </para>
    /// </summary>
    public ImmutableArray<string> InvalidTags => _invalidTags;

    /// <summary>
    /// Gets all the tags ordered by <see cref="TagInfo.CommitDateUtc"/> and then by <see cref="TagInfo.CanonicalName"/>.
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
                                                            ? (_groups = TagInfo.GetGroups( _tags ))
                                                            : _groups;

    internal IRenderable ToRenderable( ScreenType s )
    {
        var display = s.Unit;
        if( _invalidTags.Length > 0 )
        {
            display.AddBelow( InvalidTagsToRenderable( s, "ignored" ) );
        }
        var lines = s.Unit.AddBelow( GroupedTags.Select( t => t.ToRenderable( s ) ) ).TableLayout();
        display = display.AddBelow( lines );
        return display;
    }

    internal IRenderable InvalidTagsToRenderable( ScreenType s, string kind )
    {
        Throw.DebugAssert( _invalidTags.Length > 0 );
        return s.Text( $"‼ {_invalidTags.Length} {kind}:", foreColor: ConsoleColor.DarkYellow ).Box( marginRight: 1 )
                        .AddRight( RenderTagNames( s, _invalidTags ) );

        static IEnumerable<IRenderable> RenderTagNames( ScreenType s, ImmutableArray<string> names )
        {
            var sep = s.Text( ", ", TextStyle.Default );
            int i = 0;
            foreach( var n in names )
            {
                if( i++ > 0 ) yield return sep;
                yield return s.Text( n.Substring( 10 ), effect: TextEffect.Bold ); ;
            }
        }
}

}
