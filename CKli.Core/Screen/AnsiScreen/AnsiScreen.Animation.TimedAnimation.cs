using System;
using System.Threading;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed partial class Animation
    {
        sealed class TimedAnimation : IDisposable
        {
            const string _initialText = "[Working...]";
            const int _timerPeriod = 250;
            readonly Animation _a;
            readonly string _text;
            readonly Timer _timer;
            object _lock;
            int _currentColor;
            int _currentEffect;

            public TimedAnimation( Animation a )
            {
                _a = a;
                _text = _initialText;
                _lock = new object();
                _timer = new Timer( OnTimer, null, 0, _timerPeriod );
            }

            public object Lock => _lock;

            public void Animate( bool active ) => _timer.Change( active ? 0 : Timeout.Infinite, _timerPeriod );

            void OnTimer( object? state )
            {
                lock( _lock )
                {
                    if( !_a._visible ) return;
                    var w = new FixedBufferWriter( _a._workingBuffer.AsSpan() );
                    if( !_a._logLine.AppendLogLine( ref w ) )
                    {
                        return;
                    }
                    AppendMultiColor( ref w );
                    _a._target.Write( w.Text );
                }
            }

            bool AppendMultiColor( ref FixedBufferWriter w )
            {
                var text = _text.AsSpan();
                // Avoid the animation to be locked on text.Length divisor
                // of 4 for effect and 16 for colors.
                int offsetAnimation = 1 - text.Length & 1;
                _currentEffect += offsetAnimation;
                _currentColor += offsetAnimation;

                int saved = w.WrittenLength;
                foreach( var c in text )
                {
                    _currentColor = ++_currentColor & 0x0F;
                    var color = new Color( (ConsoleColor)_currentColor, (ConsoleColor)(15 - (int)_currentColor) );
                    _currentEffect = ++_currentEffect & 3;
                    var effect = _currentEffect switch
                    {
                        0 => TextEffect.Regular,
                        1 => TextEffect.Bold,
                        2 => TextEffect.Italic,
                        _ => TextEffect.Bold | TextEffect.Italic
                    };
                    if( !w.AppendCSIStyle( color, effect ) )
                    {
                        goto overflow;
                    }
                    if( w.Append( c ) )
                    {
                        saved = w.WrittenLength;
                    }
                    else
                    {
                        goto overflow;
                    }
                }
                return true;
                overflow:
                w.Truncate( saved );
                return saved > 0;
            }


            public void Dispose() => _timer.Dispose();
        }
    }
}
