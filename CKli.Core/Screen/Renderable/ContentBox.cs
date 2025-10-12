using CK.Core;
using System.Buffers;

namespace CKli.Core;

public sealed partial class ContentBox : IRenderable
{
    readonly IRenderable _content;
    readonly Padding _padding;
    readonly TextStyle _style;

    public ContentBox( IRenderable content, Padding padding, TextStyle style = default )
    {
        _content = content;
        _padding = padding;
        _style = style;
    }

    public ContentBox( IRenderable content, int top = 0, int left = 0, int bottom = 0, int right = 0 )
        : this( content, new Padding( top, left, bottom, right ) )
    {
    }

    public ContentBox( IRenderable content, TextStyle style, int top = 0, int left = 0, int bottom = 0, int right = 0 )
        : this( content, new Padding( top, left, bottom, right ), style )
    {
    }

    public int Height => _padding.Top + _content.Height + _padding.Bottom;

    public int Width => _padding.Left + _content.Width + _padding.Right;

    public TextStyle Style => _style;

    public ContentBox WithPadding( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        if( top == 0 && left == 0 && bottom == 0 && right == 0 ) return this;
        var p = new Padding( (byte)int.Clamp( top + _padding.Top, 0, 255 ),
                             (byte)int.Clamp( left + _padding.Left, 0, 255 ),
                             (byte)int.Clamp( bottom + _padding.Bottom, 0, 255 ),
                             (byte)int.Clamp( right + _padding.Right, 0, 255 ) );
        return new ContentBox( _content, p );
    }

    public ContentBox WithStyle( TextStyle style ) => _style == style ? this : new ContentBox( _content, _padding, style );

}


