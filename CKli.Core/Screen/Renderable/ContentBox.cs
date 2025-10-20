using CK.Core;
using System.Buffers;

namespace CKli.Core;

public sealed partial class ContentBox : IRenderable
{
    readonly IRenderable _content;
    readonly Filler _padding;
    readonly Filler _margin;
    readonly TextStyle _style;
    readonly int _width;
    readonly int _height;
    readonly ContentAlign _align;

    public ContentBox( IRenderable content, Filler padding, Filler margin, ContentAlign align = default, TextStyle style = default )
    {
        _content = content;
        _padding = padding;
        _margin = margin;
        _align = align;
        _style = style;
        _height = _margin.Top + _padding.Top + _content.Height + _padding.Bottom + _margin.Bottom;
        _width = _margin.Left + _padding.Left + _content.Width + _padding.Right + _margin.Right;
    }

    public ContentBox( IRenderable content,
                       int paddingTop = 0, int paddingLeft = 0, int paddingBottom = 0, int paddingRight = 0,
                       int marginTop = 0, int marginLeft = 0, int marginBottom = 0, int marginRight = 0,
                       ContentAlign align = default, TextStyle style = default )
        : this( content,
                new Filler( paddingTop, paddingLeft, paddingBottom, paddingRight ),
                new Filler( marginTop, marginLeft, marginBottom, marginRight ),
                align,
                style )
    {
    }

    public ScreenType ScreenType => _content.ScreenType;

    public int Height => _height;

    public int Width => _width;

    public Filler Padding => _padding;

    public Filler Margin => _margin;

    public IRenderable Content => _content;

    public TextStyle Style => _style;

    public ContentAlign Align => _align;

    public ContentBox WithContent( IRenderable content ) => content == _content
                                                                ? this
                                                                : new ContentBox( content, _padding, _margin, _align, _style );

    public ContentBox WithPadding( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        if( top == 0 && left == 0 && bottom == 0 && right == 0 ) return this;
        var p = new Filler( (byte)int.Clamp( top + _padding.Top, 0, 255 ),
                            (byte)int.Clamp( left + _padding.Left, 0, 255 ),
                            (byte)int.Clamp( bottom + _padding.Bottom, 0, 255 ),
                            (byte)int.Clamp( right + _padding.Right, 0, 255 ) );
        return new ContentBox( _content, p, _margin, _align, _style );
    }

    public ContentBox WithMargin( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        if( top == 0 && left == 0 && bottom == 0 && right == 0 ) return this;
        var m = new Filler( (byte)int.Clamp( top + _padding.Top, 0, 255 ),
                            (byte)int.Clamp( left + _padding.Left, 0, 255 ),
                            (byte)int.Clamp( bottom + _padding.Bottom, 0, 255 ),
                            (byte)int.Clamp( right + _padding.Right, 0, 255 ) );
        return new ContentBox( _content, _padding, m, _align, _style );
    }

    public ContentBox WithStyle( TextStyle style ) => _style == style ? this : new ContentBox( _content, _padding, _margin, _align, style );

    public ContentBox WithAlign( ContentAlign align ) => _align == align ? this : new ContentBox( _content, _padding, _margin, align, _style );

    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

}


