using System;

namespace CKli.Core;

sealed partial class AnsiScreen
{

    sealed partial class Animation
    {
        sealed class MultiColorString
        {
            readonly string _text;
            int _currentEffect;
            int _currentColor;

            public MultiColorString( string text = "[Working]" )
            {
                _text = text;
            }

            public bool Append( ref FixedBufferWriter w )
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
                    if( !w.AppendStyle( color, effect ) )
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
                if( w.AppendStyle( TextStyle.Default.Color, TextStyle.Default.Effect ) )
                {
                    return true;
                }
                overflow:
                w.Truncate( saved );
                return saved > 0;
            }
        }

    }


}
