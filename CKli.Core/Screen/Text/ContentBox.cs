using System;
using System.Buffers;

namespace CKli.Core;

public sealed class ContentBox : ILineRenderable
{
    static ReadOnlySpan<char> _whites => [' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' '];

    readonly ILineRenderable _content;
    readonly Padding _padding;

    public ContentBox( ILineRenderable content, Padding padding )
    {
        _content = content;
        _padding = padding;
    }

    public int Height => _padding.Top + _content.Height + _padding.Bottom;

    public int Width => _padding.Left + _content.Width + _padding.Right;

    public ContentBox WithPadding( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        if( top == 0 && left == 0 && bottom == 0 && right == 0 ) return this;
        var p = new Padding( (byte)int.Clamp( top + _padding.Top, 0, 255 ),
                             (byte)int.Clamp( left + _padding.Left, 0, 255 ),
                             (byte)int.Clamp( bottom + _padding.Bottom, 0, 255 ),
                             (byte)int.Clamp( right + _padding.Right, 0, 255 ) );
        return new ContentBox( _content, p );
    }


    public int RenderLine<TArg>( int i, TArg arg, ReadOnlySpanAction<char,TArg> render )
    {
        int width = Width;
        i -= _padding.Top;
        if( i < 0 || i >= _content.Height ) RenderPadding( width, arg, render );
        else
        {
            // Left alignment only (currently). 
            if( _padding.Left > 0 ) RenderPadding( _padding.Left, arg, render );
            int wC = _content.RenderLine( i, arg, render );
            int rightPad = _padding.Right + _content.Width - wC;
            if( rightPad > 0 ) RenderPadding( rightPad, arg, render );
        }
        return width;
    }

    static void RenderPadding<TArg>( int pad, TArg arg, ReadOnlySpanAction<char, TArg> render )
    {
        while( pad > _whites.Length )
        {
            render( _whites, arg );
            pad -= _whites.Length;
        }
        if( pad > 0 ) render( _whites.Slice( 0, pad ), arg );
    }

}
