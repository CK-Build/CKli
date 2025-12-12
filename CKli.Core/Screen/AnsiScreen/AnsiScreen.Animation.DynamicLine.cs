using CK.Core;
using System;
using System.Text;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed partial class Animation
    {
        sealed class DynamicLine
        {
            DynamicLine? _next;
            string _text;
            string? _lastRendered;
            bool _isGroup;

            public DynamicLine( string text, bool isGroup )
            {
                _text = text;
                _isGroup = isGroup;
            }

            public void Line( string text )
            {
                if( _next == null )
                {
                    if( _isGroup )
                    {
                        _next = new DynamicLine( text, false );
                    }
                    else
                    {
                        _text = text;
                        _lastRendered = null;
                    }
                }
                else
                {
                    _next.Line( text );
                }
            }

            public void OpenGroup( string text )
            {
                if( _next == null )
                {
                    if( _isGroup )
                    {
                        _next = new DynamicLine( text, true );
                    }
                    else
                    {
                        _isGroup = true;
                        _text = text;
                        _lastRendered = null;
                    }
                }
                else
                {
                    Throw.DebugAssert( _isGroup );
                    _next.OpenGroup( text );
                }
            }

            public bool RemoveOnCloseGroup()
            {
                Throw.DebugAssert( _isGroup || _next == null );
                if( _next != null )
                {
                    if( !_next._isGroup )
                    {
                        return true;
                    }
                    if( _next.RemoveOnCloseGroup() )
                    {
                        _next = null;
                    }
                    return false;
                }
                return true;
            }

            public void Render( ref FixedBufferWriter w, ref int depth, int width, bool refresh )
            {
                // If we hit the depth limit or we can't write the next line, give up.
                if( depth == _maxDynamicLineCount || !w.MoveToRelativeLine( 1, true ) )
                {
                    return;
                }
                // If text has not changed, we have nothing to do.
                var text = _text;
                if( _lastRendered != text || refresh )
                {
                    RenderLine( ref w, depth, width, text.AsSpan() );
                    _lastRendered = text;
                }
                ++depth;
                var n = _next;
                if( n != null )
                {
                    n.Render( ref w, ref depth, width, refresh );
                }

                static void RenderLine( ref FixedBufferWriter w, int depth, int width, ReadOnlySpan<char> t )
                {
                    t = t.Trim();
                    if( depth > 0 )
                    {
                        w.Append( ' ', depth - 1 );
                        w.Append( '╰' );
                    }
                    w.Append( '>', ' ' );
                    // Why do we need to substract 1 here (at least on Windows Terminal)?
                    int maxLen = Math.Min( width - 2 - depth - 1, w.RemainingLength );
                    if( t.Length < maxLen )
                    {
                        w.Append( t );
                    }
                    else
                    {
                        int idx = t.IndexOfAny( '\r', '\n' );
                        if( idx > 0 && idx < maxLen )
                        {
                            var head = t.Slice( 0, idx ).TrimEnd();
                            w.Append( head );
                        }
                        else
                        {
                            w.Append( t.Slice( 0, maxLen - 1 ) );
                            w.Append( '…' );
                        }
                    }
                    // Always clear the end of line.
                    w.EraseLine( CursorRelativeSpan.After );
                }
            }
        }

    }


}
