using CK.Core;
using System;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed partial class Animation
    {
        sealed class LogLine
        {
            const int _maxGroupSegment = 6;
            readonly int _maxWidth;
            readonly (int Start, int Length, string? Text)[] _segments;
            int _segmentCount;
            int _inExcessGroupCount;
            bool _lastSegmentIsLine;
            bool _dirty;

            readonly object _lockSegment;
            readonly (int Start, int Length, string Text)[] _renderSegments;
            readonly int[] _renderWidth;
            int _recomputeLevel;

            public LogLine( int maxWidth )
            {
                _maxWidth = maxWidth;
                _segments = new (int Start, int Length, string? Text)[_maxGroupSegment + 1];
                _lockSegment = new object();
                _renderSegments = new (int Start, int Length, string Text)[_maxGroupSegment + 1];
                _renderWidth = new int[_maxGroupSegment + 1];
            }

            public bool AppendLogLine( ref FixedBufferWriter w )
            {
                if( _dirty && ++_recomputeLevel > 1 )
                {
                    _dirty = false;
                    _recomputeLevel = 0;
                    return RecomputeLogLine( ref w );
                }
                return w.AppendCSIMoveToColumn( _maxWidth );
            }

            bool RecomputeLogLine( ref FixedBufferWriter w )
            {
                int count;
                lock( _lockSegment )
                {
                    Array.Copy( _segments, _renderSegments, _maxGroupSegment + 1 );
                    count = _segmentCount;
                }
                int saved = w.WrittenLength;
                w.AppendCSIStyle( TextStyle.Default.Color, TextStyle.Default.Effect );
                if( count == 0 )
                {
                    if( !w.AppendCSIEraseLine() || !w.AppendCSIMoveToColumn( _maxWidth ) )
                    {
                        goto overflow;
                    }
                }
                else
                {
                    if( !w.AppendCSIEraseLineAndMoveToFirstColumn() ) return false;
                    int mW = _maxWidth / (_segmentCount + 1);
                    int sumW = 0;
                    for( int i = 0; i < count; i++ )
                    {
                        // We inject 2 characters "> " to separate segments.
                        int len = Math.Min( 2 + _renderSegments[i].Length, mW );
                        // Ignore empty text (that should never happen).
                        if( len == 2 ) len = 0;
                        _renderWidth[i] = len;
                        sumW += len;
                    }
                    // Give the extra space to the last segment.
                    _renderWidth[count - 1] += _maxWidth - sumW;
                    for( int i = 0; i < count; i++ )
                    {
                        int width = _renderWidth[i];
                        // Ignore empty text (that should never happen).
                        if( width == 0 ) continue;
                        saved = w.WrittenLength;
                        if( !w.Append( '>', ' ' ) )
                        {
                            return true;
                        }
                        var (start, length, text) = _renderSegments[i];
                        width -= 2;
                        Throw.DebugAssert( width > 0 && length > 0 );
                        if( length >= width )
                        {
                            if( !w.Append( text.AsSpan( start, width-1 ) )
                                || !w.Append( "…" ) )
                            {
                                goto overflow;
                            }
                        }
                        else
                        {
                            if( !w.Append( text.AsSpan( start, length ) )
                                || !w.Append( ' ', width - length ) )
                            {
                                goto overflow;
                            }
                        }
                    }
                }
                return true;
                overflow:
                w.Truncate( saved );
                return false;
            }

            public void OpenGroup( string text )
            {
                if( _segmentCount == _maxGroupSegment )
                {
                    ++_inExcessGroupCount;
                    Line( text );
                }
                else
                {
                    var segment = ExtractSegment( text, out var start );
                    if( _lastSegmentIsLine )
                    {
                        lock( _lockSegment )
                        {
                            _segments[_segmentCount-1] = (start, segment.Length, text);
                        }
                    }
                    else
                    {
                        lock( _lockSegment )
                        {
                            _segments[_segmentCount++] = (start, segment.Length, text);
                        }
                    }
                    _lastSegmentIsLine = false;
                    _dirty = segment.Length > 0;
                }
            }

            public void Line( string text )
            {
                var segment = ExtractSegment( text, out var start );
                if( segment.Length == 0 ) return;
                if( _lastSegmentIsLine )
                {
                    lock( _lockSegment )
                    {
                        _segments[_segmentCount-1] = (start, segment.Length, text);
                    }
                }
                else
                {
                    lock( _lockSegment )
                    {
                        _segments[_segmentCount++] = (start, segment.Length, text);
                    }
                }
                _dirty = true;
                _lastSegmentIsLine = true;
            }

            public void CloseGroup()
            {
                // Keep dirty false if it is false here: we wait for
                // the next "positive" text change.
                if( _inExcessGroupCount > 0 )
                {
                    --_inExcessGroupCount;
                }
                else
                {
                    int toRemove = 1;
                    if( _lastSegmentIsLine )
                    {
                        Throw.DebugAssert( _segmentCount > 0 );
                        toRemove = 2;
                        _lastSegmentIsLine = false;
                    }
                    lock( _lockSegment )
                    {
                        // Handle more Close than Open.
                        _segmentCount -= toRemove;
                        if( _segmentCount < 0 ) _segmentCount = 0;
                    }
                }
            }

            static ReadOnlySpan<char> ExtractSegment( string text, out int start )
            {
                var firstLine = text.AsSpan();
                start = 0;
                while( start < firstLine.Length && char.IsWhiteSpace( firstLine[start] ) ) start++;
                firstLine = firstLine.Slice( start );
                int idx = firstLine.IndexOfAny( "\r\n" );
                if( idx > 0 )
                {
                    firstLine = firstLine.Slice( 0, idx );
                }
                firstLine = firstLine.TrimEnd();
                return firstLine;
            }
        }


    }


}
