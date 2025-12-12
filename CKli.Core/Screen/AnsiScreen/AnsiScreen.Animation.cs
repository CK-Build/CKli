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
                if( widthChange )
                {
                    w.EraseScreen( CursorRelativeSpan.After );
                }
                _workingString.Append( ref w );
                int newLineCount = 0;
                _topLine?.Render( ref w, ref newLineCount, _width, widthChange );
                if( !widthChange )
                {
                    int toRemove = _lastRenderedLineCount - newLineCount;
                    if( toRemove > 0 )
                    {
                        w.MoveToRelativeLine( toRemove, resetColumn: true );
                        while( --toRemove >= 0 )
                        {
                            w.EraseLine( CursorRelativeSpan.After );
                            w.MoveToRelativeLine( -1 );
                        }
                    }
                }
                // Restores the starting position.
                w.MoveToColumn( 1 );
                w.MoveToRelativeLine( -newLineCount );
                _lastRenderedLineCount = newLineCount;

                _target.RawWrite( w.Text );
            }
        }

        bool TrackScreenSizeChange()
        {
            _screenWidth = ConsoleScreen.GetWindowWidth();
            int width = Math.Min( _screenWidth, _maxWidth );
            if( width != _width )
            {
                _width = width;
                return true;
            }
            return false;
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
                    w.EraseScreen( CursorRelativeSpan.After );
                    w.AppendStyle( TextStyle.Default.Color, TextEffect.Regular );
                    w.ShowCursor( true );

                    _target.RawWrite( w.Text );
                }
            }
        }

    }


}
