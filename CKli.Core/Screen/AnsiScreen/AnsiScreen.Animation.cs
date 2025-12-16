using System;
using System.Threading;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    /// <summary>
    /// This has been designed to be lock-free when handling logs.
    /// The lock is used only from the Timer and in Show or Hide.
    /// This relies on the single linked-list of DynamicLine.
    /// Maximum number of lines management is subtle (_maxDynamicLineCount = 10)
    /// and the code doesn't blindly update the screen...
    /// Please don't change the code unless you truly understand the pattern.
    /// </summary>
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

        VerticalContent? _header;
        VerticalContent? _lastRenderedHeader;
        int _lastHeaderHeight;
        int _screenWidth;
        int _width;
        DynamicLine? _topLine;
        DynamicLine? _lastRenderedTopLine;
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

        public void AddHeader( IRenderable h )
        {
            if( h.Height == 0 ) return;
            if( _header == null )
            {
                var header = new VerticalContent( _screenType );
                _header = header.Append( [h] );
            }
            else
            {
                _header = _header.Append( [h] );
            }
        }

        /// <summary>
        /// Clears the current header and returns it.
        /// </summary>
        /// <returns>The current header.</returns>
        public VerticalContent? ClearHeader()
        {
            var h = _header;
            _header = null;
            return h;
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
                    _timer.Change( _timerPeriod, _timerPeriod );
                    Draw();
                }
            }
        }

        void OnTimer( object? state )
        {
            lock( _lock )
            {
                if( !_visible ) return;
                Draw();
            }
        }

        void Draw()
        {
            bool refresh = TrackScreenSizeChange();
            var w = new FixedBufferWriter( _workingBuffer.AsSpan() );

            HandleHeader( ref w, ref refresh, _header );

            int newLineCount = 0;
            DynamicLine? newTopLine = _lastRenderedTopLine;
            // Capture the topLine.
            var topLine = _topLine;
            if( topLine != null )
            {
                int skipTopLines = Math.Max( topLine.GetDepth() - _maxDynamicLineCount, 0 );
                topLine.Render( ref w, ref newLineCount, _width, ref refresh, skipTopLines, ref newTopLine, _workingString );
                newLineCount -= skipTopLines;
            }
            else
            {
                _workingString.Append( ref w );
            }
            if( !refresh )
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
            _lastRenderedLineCount = newLineCount;
            _lastRenderedTopLine = newTopLine;

            // Restores the starting position.
            w.MoveToColumn( 1 );
            w.MoveToRelativeLine( -(newLineCount + _lastHeaderHeight) );

            _target.RawWrite( w.Text );
        }

        void HandleHeader( ref FixedBufferWriter w, ref bool refresh, VerticalContent? header )
        {
            bool headerChanged = _lastRenderedHeader != header;
            bool headerRedrawn = false;
            if( refresh || headerChanged )
            {
                headerRedrawn = HandleHeaderChange( ref w, header, ref refresh, headerChanged );
            }
            if( !headerRedrawn )
            {
                w.MoveToRelativeLine( _lastHeaderHeight );
                if( refresh )
                {
                    w.EraseScreen( CursorRelativeSpan.After );
                }
            }
        }

        bool HandleHeaderChange( ref FixedBufferWriter w, VerticalContent? header, ref bool refresh, bool headerChanged )
        {
            if( header != null )
            {
                var resized = header.SetWidth( _width, false );
                // It's hard to really say but it seems that this optimization weakens
                // the size changed detection (since this relies on the same height and
                // avoids a brutal clear of the screen from the top position)...
                //
                //if( !headerChanged && _lastHeaderHeight == resized.Height )
                //{
                //    // No actual change in the header. We can simply skip the header lines
                //    // and let the dynamic part do its job.
                //    return false;
                //}
                // Header must be redrawn.
                _target.BeginUpdate();
                w.EraseScreen( CursorRelativeSpan.After );
                _target.RawWrite( w.Text );
                w.ResetBuffer();
                resized.Render( _target );
                _target.EndUpdate();
                _lastHeaderHeight = resized.Height;
                // We have cleared the screen:
                refresh = true;
                _lastRenderedHeader = header;
                return true;
            }
            // No current header.
            if( headerChanged )
            {
                // We had one before. Let's clear the screen here and consider
                // that we have redrawn the header.
                _lastHeaderHeight = 0;
                _lastRenderedHeader = null;
                w.EraseScreen( CursorRelativeSpan.After );
                refresh = true;
                return true;
            }
            return false;
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

                    bool refresh = TrackScreenSizeChange();

                    var w = new FixedBufferWriter( _workingBuffer.AsSpan() );
                    HandleHeader( ref w, ref refresh, _header );
                    w.Append( AnsiCodes.RemoveProgressIndicator() );
                    w.AppendStyle( TextStyle.Default.Color, TextEffect.Regular );
                    w.ShowCursor( true );

                    _target.RawWrite( w.Text );
                }
            }
        }

    }


}
