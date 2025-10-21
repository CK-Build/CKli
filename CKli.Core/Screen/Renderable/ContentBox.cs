using CK.Core;

namespace CKli.Core;

public sealed partial class ContentBox : IRenderable
{
    readonly IRenderable _content;
    readonly ContentBox? _origin;
    readonly Filler _padding;
    readonly Filler _margin;
    readonly TextStyle _style;
    readonly int _width;
    readonly int _height;
    readonly ContentAlign _align;

    public ContentBox( IRenderable content, Filler padding, Filler margin, ContentAlign align = default, TextStyle style = default )
        : this( null, content, padding, margin, align, style )
    {
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

    ContentBox( ContentBox? origin, IRenderable content, Filler padding, Filler margin, ContentAlign align, TextStyle style )
    {
        Throw.DebugAssert( origin == null || origin._origin == null );
        _content = content;
        _origin = origin;
        _padding = padding;
        _margin = margin;
        _align = align;
        _style = style;
        _height = margin.Top + padding.Top + content.Height + padding.Bottom + margin.Bottom;
        _width = margin.Left + padding.Left + content.Width + padding.Right + margin.Right;
    }

    public ScreenType ScreenType => _content.ScreenType;

    public int Height => _height;

    public int Width => _width;

    public int MinWidth => _origin == null
                            ? (_margin.Left + _padding.Left > 0 ? 1 : 0) + _content.MinWidth + (_padding.Right + _margin.Right > 0 ? 1 : 0)
                            : _origin.MinWidth;

    public Filler Padding => _padding;

    public Filler Margin => _margin;

    public IRenderable Content => _content;

    public TextStyle Style => _style;

    public ContentAlign Align => _align;

    public ContentBox WithContent( IRenderable content ) => content == _content
                                                                ? this
                                                                : _origin == null
                                                                    ? new ContentBox( null, content, _padding, _margin, _align, _style )
                                                                    : new ContentBox( _origin.WithContent( content ), content, _padding, _margin, _align, _style );

    public ContentBox AddPadding( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        if( top == 0 && left == 0 && bottom == 0 && right == 0 ) return this;

        int vLeft = int.Clamp( left + _padding.Left, 0, short.MaxValue );
        int vRight = int.Clamp( right + _padding.Right, 0, short.MaxValue );
        // Preserves at least a single padding if the origin has padding (on both sides).
        // (The left and right margins can reach 0 if needed).
        if( _origin != null )
        {
            if( vLeft == 0 && _origin._padding.Left > 0 )
            {
                vLeft = 1;
            }
            if( vRight == 0 && _origin._padding.Right > 0 )
            {
                vLeft = 1;
            }
        }
        var p = new Filler( (short)int.Clamp( top + _padding.Top, 0, short.MaxValue ),
                            (short)vLeft,
                            (short)int.Clamp( bottom + _padding.Bottom, 0, short.MaxValue ),
                            (short)vRight );
        return new ContentBox( _origin, _content, p, _margin, _align, _style );
    }

    public ContentBox AddMargin( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        if( top == 0 && left == 0 && bottom == 0 && right == 0 ) return this;
        int vLeft = int.Clamp( left + _margin.Left, 0, short.MaxValue );
        int vRight = int.Clamp( right + _margin.Right, 0, short.MaxValue );
        // Preserves at least a single margin if the origin has margin but no padding (on both sides).
        if( _origin != null )
        {
            if( vLeft == 0 && _origin._margin.Left > 0 && _origin._padding.Left == 0 )
            {
                vLeft = 1;
            }
            if( vRight == 0 && _origin._margin.Right > 0 && _origin._padding.Right == 0 )
            {
                vLeft = 1;
            }
        }
        var m = new Filler( (short)int.Clamp( top + _margin.Top, 0, short.MaxValue ),
                            (short)vLeft,
                            (short)int.Clamp( bottom + _margin.Bottom, 0, short.MaxValue ),
                            (short)vRight );
        return new ContentBox( _origin, _content, _padding, m, _align, _style );
    }

    public ContentBox WithStyle( TextStyle style ) => _style == style
                                                        ? this
                                                        : _origin == null
                                                            ? new ContentBox( null, _content, _padding, _margin, _align, style )
                                                            : new ContentBox( _origin.WithStyle( style ), _content, _padding, _margin, _align, style );

    public ContentBox WithAlign( ContentAlign align ) => _align == align
                                                            ? this
                                                            : _origin == null
                                                                ? new ContentBox( null, _content, _padding, _margin, align, _style )
                                                                : new ContentBox( _origin.WithAlign( align ), _content, _padding, _margin, align, _style );

    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

}


