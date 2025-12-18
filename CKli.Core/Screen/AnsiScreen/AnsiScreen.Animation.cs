using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using static System.Reflection.Metadata.BlobBuilder;

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
        readonly Lock _lock;

        List<IRenderable>? _logs;
        VerticalContent? _finalLogs;
        int _screenWidth;
        int _width;
        DynamicLine? _topLine;
        DynamicLine? _lastRenderedTopLine;
        int _lastRenderedLineCount;
        bool _visible;
        bool _finalHidden;

        public int ScreenWidth => _screenWidth;

        public Animation( RenderTarget target )
        {
            _target = target;
            // Big enough buffer.
            _workingBuffer = new char[ _maxWidth * 3 * _maxDynamicLineCount];
            _lock = new Lock();
            _workingString = new MultiColorString();
            _timer = new Timer( OnTimer, null, Timeout.Infinite, _timerPeriod );
            Show();
        }

        public void Dispose()
        {
            Hide( true );
            _timer.Dispose();
        }

        public void AddLog( IRenderable h )
        {
            if( h.Height == 0 ) return;
            _logs ??= new List<IRenderable>();
            _logs.Add( h );
        }

        /// <summary>
        /// Clears the current logs and returns them.
        /// </summary>
        /// <returns>The current logs.</returns>
        public VerticalContent? ClearLogs()
        {
            var h = _finalLogs;
            _finalLogs = null;
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

        public void Show()
        {
            lock( _lock )
            {
                if( !_visible && !_finalHidden )
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
            bool refresh = TrackScreenWidthChange();
            var w = new FixedBufferWriter( _workingBuffer.AsSpan() );

            _workingString.Append( ref w );
            w.Append( '\n' );

            int newLineCount = 0;
            DynamicLine? newTopLine = _lastRenderedTopLine;
            // Capture the topLine.
            var topLine = _topLine;
            if( topLine != null )
            {
                int skipTopLines = Math.Max( topLine.GetDepth() - _maxDynamicLineCount, 0 );
                topLine.Render( ref w, ref newLineCount, _width, ref refresh, skipTopLines, ref newTopLine );
                newLineCount -= skipTopLines;
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
            w.MoveToRelativeLine( -(1 + newLineCount) );

            _target.RawWrite( w.Text );
        }

        bool TrackScreenWidthChange()
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

        public void Hide( bool final )
        {
            lock( _lock )
            {
                if( _visible )
                {
                    _visible = false;
                    _timer.Change( Timeout.Infinite, _timerPeriod );

                    bool refresh = TrackScreenWidthChange();

                    var w = new FixedBufferWriter( _workingBuffer.AsSpan() );
                    w.EraseScreen( CursorRelativeSpan.After );
                    w.Append( AnsiCodes.RemoveProgressIndicator() );
                    w.AppendStyle( TextStyle.Default.Color, TextEffect.Regular );
                    w.ShowCursor( true );
                    _target.RawWrite( w.Text );
                    _lastRenderedLineCount = 0;
                    _lastRenderedTopLine = null;
                    if( final )
                    {
                        if( _logs != null && _logs.Count > 0 )
                        {
                            _finalLogs = new VerticalContent( _screenType, _logs.Select( l => l.SetWidth( _screenWidth, false ) ).ToImmutableArray() );
                            _finalLogs.Render( _target );
                            _logs.Clear();
                        }
                        _finalHidden = true;
                    }
                }
            }
        }

        public void Resurrect() => _finalHidden = false;
    }


}
