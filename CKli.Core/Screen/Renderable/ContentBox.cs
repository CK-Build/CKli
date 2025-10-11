using CK.Core;
using System;
using System.Buffers;

namespace CKli.Core;

public sealed class ContentBox : IRenderable
{
    static ReadOnlySpan<char> _whites => [' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' '];

    readonly IRenderable _content;
    readonly Padding _padding;
    readonly Color? _color;

    public ContentBox( IRenderable content, Padding padding, Color? color = null )
    {
        _content = content;
        _padding = padding;
        _color = color;
    }

    public ContentBox( IRenderable content, int top = 0, int left = 0, int bottom = 0, int right = 0 )
        : this( content, new Padding( top, left, bottom, right ) )
    {
    }

    public ContentBox( IRenderable content, Color color, int top = 0, int left = 0, int bottom = 0, int right = 0 )
        : this( content, new Padding( top, left, bottom, right ), color )
    {
    }

    public int Height => _padding.Top + _content.Height + _padding.Bottom;

    public int Width => _padding.Left + _content.Width + _padding.Right;

    public Color? Color => _color;

    public ContentBox WithPadding( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        if( top == 0 && left == 0 && bottom == 0 && right == 0 ) return this;
        var p = new Padding( (byte)int.Clamp( top + _padding.Top, 0, 255 ),
                             (byte)int.Clamp( left + _padding.Left, 0, 255 ),
                             (byte)int.Clamp( bottom + _padding.Bottom, 0, 255 ),
                             (byte)int.Clamp( right + _padding.Right, 0, 255 ) );
        return new ContentBox( _content, p );
    }

    public ContentBox WithColor( Color? color )
    {
        if( _color == color ) return this;
        return new ContentBox( _content, _padding, color );
    }

    public int RenderLine( int line, IRenderTarget target, RenderContext context )
    {
        Throw.DebugAssert( line >= 0 );
        var textStyle = context.GetTextStyle( _color, out Color previousColor );
        int width = Width;
        line -= _padding.Top;
        if( line >= _content.Height ) RenderPadding( width, target, textStyle );
        else
        {
            // Left alignment only (currently). 
            if( _padding.Left > 0 ) RenderPadding( _padding.Left, target, textStyle );
            int wC = _content.RenderLine( line, target, context );
            int rightPad = _padding.Right + _content.Width - wC;
            if( rightPad > 0 ) RenderPadding( rightPad, target, textStyle );
        }
        context.RestoreColor( previousColor );
        return width;
    }

    static void RenderPadding( int pad, IRenderTarget target, TextStyle style )
    {
        while( pad > _whites.Length )
        {
            target.Append( _whites, style );
            pad -= _whites.Length;
        }
        if( pad > 0 ) target.Append( _whites.Slice( 0, pad ), style );
    }

}


