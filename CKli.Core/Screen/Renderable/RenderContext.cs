using System;

namespace CKli.Core;

public sealed class RenderContext
{
    TextStyle _textStyle;

    internal RenderContext( IRenderable root )
    {
        _textStyle = TextStyle.Default;
    }

    internal void EndOfLine()
    {
        _textStyle = TextStyle.Default;
    }

    internal TextStyle GetTextStyle( TextEffect effect )
    {
        return _textStyle = _textStyle.With( effect );
    }

    internal TextStyle GetTextStyle( Color? color, out Color previousColor )
    {
        previousColor = _textStyle.Color;
        if( color.HasValue )
        {
            _textStyle = _textStyle.With( color.Value );
        }
        return _textStyle;
    }

    internal void RevertColor( Color previousColor )
    {
    }

}
