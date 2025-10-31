using CK.Core;
using System;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed partial class Animation : IDisposable
    {
        readonly RenderTarget _target;
        readonly char[] _workingBuffer;
        readonly int _width;
        readonly TimedAnimation _timedAnimation;
        readonly LogLine _logLine;
        bool _visible;

        public Animation( RenderTarget target, int width )
        {
            _target = target;
            _width = Math.Min( width, 180 );
            // Big enough buffer.
            _workingBuffer = new char[width * 6 + 100];
            _visible = true;
            // The [Working...] is 12 characters.
            _logLine = new LogLine( width - 12 );
            _timedAnimation = new TimedAnimation( this );
            target.Write( AnsiCodes.SetProgressIndicator() );
        }

        public void Dispose()
        {
            _timedAnimation.Dispose();
            Hide();
        }

        public void OpenGroup( LogLevel level, string text )
        {
            _logLine.OpenGroup( text );
            Show();
        }

        public void CloseGroup()
        {
            _logLine.CloseGroup();
            Show();
        }
        
        public void Line( LogLevel level, string text )
        {
            _logLine.Line( text );
            Show();
        }

        public void Hide()
        {
            lock( _timedAnimation.Lock )
            {
                if( _visible )
                {
                    _visible = false;
                    _timedAnimation.Animate( false );
                    var w = new FixedBufferWriter( _workingBuffer.AsSpan() );
                    w.AppendCSIStyle( TextStyle.Default.Color, TextEffect.Regular );
                    w.AppendCSIEraseLineAndMoveToFirstColumn();
                    w.Append( AnsiCodes.RemoveProgressIndicator() );
                    _target.Write( w.Text );
                }
            }
        }

        void Show()
        {
            lock( _timedAnimation.Lock )
            {
                if( !_visible )
                {
                    _visible = true;
                    _target.Write( AnsiCodes.SetProgressIndicator() );
                    _timedAnimation.Animate( true );
                }
            }
        }


    }


}
