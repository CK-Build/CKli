using CK.Core;
using System;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed partial class Animation
    {
        /// <summary>
        /// Pure single linked list: this is the key for lock-free log collection.
        /// </summary>
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

            public int GetDepth()
            {
                int d = 1;
                var n = _next;
                while( n != null )
                {
                    d++;
                    n = n._next;
                }
                return d;
            }

            public void Render( ref FixedBufferWriter w, ref int depth, int width, ref bool refresh, int skipTopLines, ref DynamicLine? topLine, MultiColorString workingString )
            {
                int depthInRange = depth - skipTopLines;
                if( depthInRange >= 0 )
                {
                    // Are we the starting line?
                    if( depthInRange == 0 )
                    {
                        // If yes, then we must:
                        // - Check that we are the last one and if not: refresh!
                        // - Display the MultiColorString.
                        if( topLine != this )
                        {
                            // We are the new top line. If another were here, then we clear the screen
                            // and switch to refresh mode (if not already).
                            if( topLine != null && !refresh )
                            {
                                w.EraseScreen( CursorRelativeSpan.After );
                                refresh = true;
                            }
                            topLine = this;
                        }
                        workingString.Append( ref w );
                    }
                    else if( depthInRange == _maxDynamicLineCount )
                    {
                        // If we hit the depth limit, give up.
                        return;
                    }
                    // If we can't write the next line, give up.
                    // Don't use w.MoveToRelativeLine( 1, true ) here: this doesn't trigger
                    // the scroll of the viewport. Using '\n' does.
                    if( !w.Append( '\n' ) )
                    {
                        return;
                    }
                    // If text has not changed, we have nothing to do (unless refresh is true).
                    var text = _text;
                    if( _lastRendered != text || refresh )
                    {
                        RenderLine( ref w, depth, width, text.AsSpan() );
                        _lastRendered = text;
                    }
                }
                // Whether we are in range or not, call the next.
                ++depth;
                _next?.Render( ref w, ref depth, width, ref refresh, skipTopLines, ref topLine, workingString );

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
