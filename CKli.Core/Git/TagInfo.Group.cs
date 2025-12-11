using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CKli.Core;

public sealed partial class TagInfo
{
    /// <summary>
    /// Groups <see cref="TagInfo"/> by commits and commit dates.
    /// </summary>
    public sealed class Group : IComparable<Group>
    {
        readonly ImmutableArray<TagInfo> _sortedTags;
        readonly int _idx;
        int _tagCount;

        internal Group( ImmutableArray<TagInfo> sortedTags, int idx )
        {
            _sortedTags = sortedTags;
            _idx = idx;
            _tagCount = 1;
        }

        internal void Add() => ++_tagCount;

        internal TagInfo Head => _sortedTags[_idx];

        /// <summary>
        /// Gets the target commit. Null for a remote tag with a target that doesn't exist locally (a fetch is required).
        /// </summary>
        public Commit? Commit => Head.Commit;

        /// <summary>
        /// Gets at least one <see cref="TagInfo"/> for this <see cref="Commit"/>.
        /// </summary>
        public IEnumerable<TagInfo> Tags => _sortedTags.Skip( _idx ).Take( _tagCount );

        /// <summary>
        /// Gets <see cref="Tags"/> as a span.
        /// </summary>
        public ReadOnlySpan<TagInfo> TagSpan => _sortedTags.AsSpan( _idx, _tagCount );

        /// <summary>
        /// Gets the number of tags.
        /// </summary>
        public int TagCount => _tagCount;

        public int CompareTo( Group? other ) => CompareCommit( Commit, other?.Commit );

        public override string ToString() => Commit == null ? Head.ToString() : $"{Commit} - {_tagCount} tags.";

        internal IRenderable ToRenderable( ScreenType s, IRenderable? beforeTags = null )
        {
            if( _tagCount == 1 ) return Head.ToRenderable( s );
            return s.Unit.AddRight( Head.GetRenderableCommit( s ).Box( marginRight: 1 ),
                                    s.Unit.AddRight( beforeTags, _tagCount == 1
                                                                    ? Head.GetRenderableTagName( s )
                                                                    : TagInfo.RenderTagNames( s, Tags ) ).Box()
                                  );
        }

    }

    internal static ImmutableArray<Group> GetGroups( ImmutableArray<TagInfo> sortedTags, out int remoteOnly )
    {
        var result = ImmutableArray.CreateBuilder<Group>();
        int i = 0;
        for( ; i < sortedTags.Length; i++ )
        {
            TagInfo? tag = sortedTags[i];
            if( tag.Commit != null )
            {
                break;
            }
            result.Add( new Group( sortedTags, i ) );
        }
        remoteOnly = i;
        Group? current = null;
        for( ; i < sortedTags.Length; i++ )
        {
            TagInfo? tag = sortedTags[i];
            Throw.DebugAssert( tag.Commit != null );
            if( current == null || current.Commit!.Sha != tag.Commit.Sha )
            {
                current = new Group( sortedTags, i );
                result.Add( current );
            }
            else current.Add();
        }
        return result.DrainToImmutable();
    }

}
