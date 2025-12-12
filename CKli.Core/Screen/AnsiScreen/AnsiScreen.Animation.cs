using CK.Core;
using System;
using System.Text;
using System.Threading;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed partial class Animation : IDisposable
    {
        const int _timerPeriod = 250;
        const int _maxWidth = 240;
        const int _maxDynamicLineCount = 10;

        readonly RenderTarget _target;
        readonly Timer _timer;
        readonly MultiColorString _workingString;
        readonly char[] _workingBuffer;
        readonly int[] _linesLength;
        readonly object _lock;

        int _screenWidth;
        int _width;
        DynamicLine? _topLine;
        int _lastRenderedLineCount;
        bool _visible;

        public int ScreenWidth => _screenWidth;

        public Animation( RenderTarget target )
        {
            _target = target;
            // Big enough buffer.
            _workingBuffer = new char[ _maxWidth * 3 * _maxDynamicLineCount];
            _lock = new object();
            _workingString = new MultiColorString();
            _linesLength = new int[ _maxDynamicLineCount ];
            _timer = new Timer( OnTimer, null, Timeout.Infinite, _timerPeriod );
            Show();
        }

        public void Dispose()
        {
            Hide();
            _timer.Dispose();
        }

        public void OnLog( string? text, bool isOpenGroup )
        {
            if( text == null )
            {
                if( _topLine != null )
                {
                    if( _topLine.RemoveOnCloseGroup() )
                    {
                        _topLine = null;
                    }
                }
            }
            else
            {
                if( _topLine == null )
                {
                    _topLine = new DynamicLine( text, isOpenGroup );
                }
                else
                {
                    if( isOpenGroup )
                    {
                        _topLine.OpenGroup( text );
                    }
                    else
                    {
                        _topLine.Line( text );
                    }
                }
            }
            Show();
        }

        void Show()
        {
            lock( _lock )
            {
                if( !_visible )
                {
                    _visible = true;
                    _screenWidth = ConsoleScreen.GetWindowWidth();
                    var w = new FixedBufferWriter( _workingBuffer.AsSpan() );
                    w.AppendStyle( TextStyle.Default.Color, TextEffect.Regular );
                    w.ShowCursor( false );
                    _target.RawWrite( w.Text );
                    _timer.Change( 0, _timerPeriod );
                }
            }
        }

        void OnTimer( object? state )
        {
            lock( _lock )
            {
                if( !_visible ) return;

                bool widthChange = TrackScreenSizeChange();
                var w = new FixedBufferWriter( _workingBuffer.AsSpan() );
                RestoreStartPosition( ref w );
                _workingString.Append( ref w );
                w.EraseLine( CursorRelativeSpan.After );
                int newLineCount = 0;
                _topLine?.Render( ref w, ref newLineCount, _width, widthChange, _linesLength );
                int toRemove = _lastRenderedLineCount - newLineCount;
                if( toRemove > 0 )
                {
                    w.MoveToRelativeLine( toRemove );
                    EraseLastLines( ref w, toRemove );
                }
                _lastRenderedLineCount = newLineCount;
                //w.MoveToColumn( 1 );
                //w.MoveToRelativeLine( -newLineCount );

                _target.RawWrite( w.Text );
            }
        }

        bool TrackScreenSizeChange()
        {
            bool widthChange = false;
            _screenWidth = ConsoleScreen.GetWindowWidth();
            int width = Math.Min( _screenWidth, _maxWidth );
            int delta = width - _width;
            if( delta != 0 )
            {
                widthChange = true;
                if( delta < 0 )
                {
                    // The screen is smaller: some rendered lines have been wrapped.
                    int newlastRenderedLineCount = _lastRenderedLineCount;
                    for( int i = 0; i < _lastRenderedLineCount; ++i )
                    {
                        int len = _linesLength[i];
                        while( len > width )
                        {
                            len -= width;
                            newlastRenderedLineCount++;
                        }
                    }
                    _lastRenderedLineCount = newlastRenderedLineCount;
                }
                _width = width;
            }

            return widthChange;
        }

        void RestoreStartPosition( ref FixedBufferWriter w )
        {
            w.MoveToColumn( 1 );
            w.MoveToRelativeLine( -_lastRenderedLineCount );
        }

        static void EraseLastLines( ref FixedBufferWriter w, int count )
        {
            Throw.DebugAssert( count > 0 );
            w.MoveToColumn( 1 );
            while( --count >= 0 )
            {
                w.EraseLine( CursorRelativeSpan.After );
                w.MoveToRelativeLine( -1 );
            }
        }

        public void Hide()
        {
            lock( _lock )
            {
                if( _visible )
                {
                    _visible = false;
                    _timer.Change( Timeout.Infinite, _timerPeriod );

                    TrackScreenSizeChange();

                    var w = new FixedBufferWriter( _workingBuffer.AsSpan() );
                    w.Append( AnsiCodes.RemoveProgressIndicator() );

                    if( _lastRenderedLineCount > 0 )
                    {
                        EraseLastLines( ref w, _lastRenderedLineCount );
                        _lastRenderedLineCount = 0;
                    }
                    else
                    {
                        // We have not rendered lines: resets the cursor to
                        // erase the MultiColorString.
                        w.MoveToColumn( 1 );
                    }
                    // Erase the MultiColorString.
                    w.EraseLine( CursorRelativeSpan.After );
                    w.AppendStyle( TextStyle.Default.Color, TextEffect.Regular );
                    w.ShowCursor( true );

                    _target.RawWrite( w.Text );
                }
            }
        }

    }


}
