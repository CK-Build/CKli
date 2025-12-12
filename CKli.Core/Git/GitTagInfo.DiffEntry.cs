using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Core;

public sealed partial class GitTagInfo
{
    /// <summary>
    /// For each <see cref="Commit"/>, captures the <see cref="Tags"/> with their potential differences.
    /// </summary>
    public readonly struct DiffEntry
    {
        readonly TagInfo.Group _group;
        readonly IReadOnlyList<LocalRemoteTag> _tags;

        internal DiffEntry( IReadOnlyDictionary<string, TagInfo> localIndex,
                            TagInfo.Group? gLocal,
                            IReadOnlyDictionary<string, TagInfo> remoteIndex,
                            TagInfo.Group? gRemote,
                            ref Diff.Stats stats )
        {
            Throw.DebugAssert( gLocal != null || gRemote != null );
            _group = gLocal ?? gRemote!;
            Throw.DebugAssert( "Fetch required tags have been handled.", _group.Commit != null );
            if( gLocal == null )
            {
                Throw.DebugAssert( gRemote != null );
                _group = gRemote;
                _tags = SingleSide( gRemote, TagDiff.RemoteOnly, localIndex, ref stats );
            }
            else
            {
                _group = gLocal;
                if( gRemote == null )
                {
                    _tags = SingleSide( gLocal, TagDiff.LocalOnly, remoteIndex, ref stats );
                }
                else
                {
                    var tags = new List<LocalRemoteTag>( gLocal.TagCount );
                    var lTags = gLocal.TagSpan;
                    var rTags = gRemote.TagSpan;
                    int iL = -1;
                    int iR = 0;
                    while( ++iL < lTags.Length )
                    {
                        if( iR >= rTags.Length )
                        {
                            tags.Add( new LocalRemoteTag( lTags[iL], TagDiff.LocalOnly, remoteIndex, ref stats ) );
                        }
                        else
                        {
                            int cmp = StringComparer.Ordinal.Compare( lTags[iL].CanonicalName, rTags[iR].CanonicalName );
                            while( cmp > 0 )
                            {
                                tags.Add( new LocalRemoteTag( rTags[iR], TagDiff.RemoteOnly, localIndex, ref stats ) );
                                if( ++iR >= rTags.Length )
                                {
                                    tags.Add( new LocalRemoteTag( lTags[iL], TagDiff.LocalOnly, remoteIndex, ref stats ) );
                                    break;
                                }
                                cmp = StringComparer.Ordinal.Compare( lTags[iL].CanonicalName, rTags[iR].CanonicalName );
                            }
                            if( iR < rTags.Length )
                            {
                                if( cmp == 0 )
                                {
                                    var l = lTags[iL];
                                    var r = rTags[iR];
                                    TagDiff d = TagDiff.None;
                                    if( l.Annotation != null )
                                    {
                                        if( r.Annotation != null )
                                        {
                                            // Two annotated tags.
                                            if( l.Annotation.Message != r.Annotation.Message )
                                            {
                                                d |= TagDiff.AnnotationMessageDiffer;
                                            }
                                            if( !l.Annotation.Tagger.Equals( r.Annotation.Tagger ) )
                                            {
                                                d |= TagDiff.AnnotationTaggerDiffer;
                                            }
                                        }
                                        else
                                        {
                                            d |= TagDiff.LocalAnnotatedDiffer;
                                        }
                                    }
                                    else if( r.Annotation != null )
                                    {
                                        d |= TagDiff.RemoteAnnotatedDiffer;
                                    }
                                    Throw.DebugAssert( "There is no TagDiff.CommitConflict: the tags target the same commit.",
                                                        localIndex[r.CanonicalName] == l && remoteIndex[l.CanonicalName] == r );
                                    ++stats._commonCount;
                                    if( d != TagDiff.None ) ++stats._differCount;
                                    tags.Add( new LocalRemoteTag( l, r, d ) );
                                    iR++;
                                }
                                else
                                {
                                    Throw.DebugAssert( cmp < 0 );
                                    tags.Add( new LocalRemoteTag( lTags[iL], TagDiff.LocalOnly, remoteIndex, ref stats ) );
                                }
                            }
                        }
                    }
                    while( iR < rTags.Length )
                    {
                        tags.Add( new LocalRemoteTag( rTags[iR], TagDiff.RemoteOnly, localIndex, ref stats ) );
                        iR++;
                    }
                    _tags = tags;
                }
            }

            static LocalRemoteTag[] SingleSide( TagInfo.Group g,
                                                TagDiff diff,
                                                IReadOnlyDictionary<string, TagInfo> oppositeIndex,
                                                ref Diff.Stats stats )
            {
                var tags = new LocalRemoteTag[g.TagCount];
                for( int i = 0; i < g.TagSpan.Length; ++i )
                {
                    tags[i] = new LocalRemoteTag( g.TagSpan[i], diff, oppositeIndex, ref stats );
                }
                return tags;
            }
        }

        /// <summary>
        /// Gets the target commit.
        /// </summary>
        public Commit Commit => _group.Commit!;

        /// <summary>
        /// Gets the local/remote tags and their <see cref="TagDiff"/> for this <see cref="Commit"/>.
        /// </summary>
        public IReadOnlyList<LocalRemoteTag> Tags => _tags;

        public override string ToString() => $"{Commit} - {_tags.Count} local/remote tags.";
    }
}
