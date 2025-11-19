using CK.Core;
using System;

namespace CKli.Core;

/// <summary>
/// A Box wraps a content and can have margins, paddings and handles content alignment.
/// </summary>
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

    /// <summary>
    /// Initializes a new <see cref="ContentBox"/>.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="padding">The padding.</param>
    /// <param name="margin">The margin.</param>
    /// <param name="align">The content alignment.</param>
    /// <param name="style">The content style.</param>
    public ContentBox( IRenderable content, Filler padding, Filler margin, ContentAlign align = default, TextStyle style = default )
        : this( null, content, padding, margin, align, style )
    {
    }

    /// <summary>
    /// Initializes a new <see cref="ContentBox"/>.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="paddingTop">The top padding.</param>
    /// <param name="paddingLeft">The left padding.</param>
    /// <param name="paddingBottom">The bottom padding.</param>
    /// <param name="paddingRight">The right padding.</param>
    /// <param name="marginTop">The top margin.</param>
    /// <param name="marginLeft">The left margin.</param>
    /// <param name="marginBottom">The bottom margin.</param>
    /// <param name="marginRight">The right margin.</param>
    /// <param name="align">The content alignment.</param>
    /// <param name="style">The content style.</param>
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

    /// <inheritdoc />
    public ScreenType ScreenType => _content.ScreenType;

    /// <inheritdoc />
    public int Height => _height;

    /// <inheritdoc />
    public int Width => _width;

    /// <inheritdoc />
    public int MinWidth => _origin == null
                            ? (_margin.Left + _padding.Left > 0 ? 1 : 0) + _content.MinWidth + (_padding.Right + _margin.Right > 0 ? 1 : 0)
                            : _origin.MinWidth;

    /// <inheritdoc />
    public int NominalWidth => _origin == null
                                ? (_margin.Left + _padding.Left + _content.NominalWidth + _padding.Right + _margin.Right)
                                : _origin.NominalWidth;

    /// <summary>
    /// Gets the padding.
    /// </summary>
    public Filler Padding => _padding;

    /// <summary>
    /// Gets the margin.
    /// </summary>
    public Filler Margin => _margin;

    /// <summary>
    /// Gets this Box's content.
    /// </summary>
    public IRenderable Content => _content;

    /// <summary>
    /// Gets the text style to apply to the content (including paddings but not margins).
    /// </summary>
    public TextStyle Style => _style;

    /// <summary>
    /// Gets how the content must be aligned in this Box.
    /// </summary>
    public ContentAlign Align => _align;

    IRenderable IRenderable.SetWidth( int width, bool allowWider ) => SetWidth( width, allowWider );

    /// <inheritdoc />
    public ContentBox SetWidth( int width, bool allowWider = true )
    {
        int minWidth = MinWidth;
        if( width < minWidth ) width = minWidth;
        if( width == _width ) return this;
        if( _origin != null ) return _origin.SetWidth( width, allowWider );
        int delta = width - NominalWidth;
        if( delta > 0 )
        {
            // The requested width is larger than the NominalWitdh: we use our margin
            // to satisfy the request (accounting the alignment).
            // Note that it is this enclosing Box that adjusts its width: the content is
            // set to its NominalWidth.
            return allowWider
                    ? BiggerAdjust( delta, _content.SetWidth( _content.NominalWidth, false ) )
                    : this;
        }
        if( width == minWidth )
        {
            // The requested width is smaller or equal to the minimal width: the content must be set to its
            // own minimal width and we cancel our margins and ultimately consider settting the padding or
            // the margin to 1 to preserve the left or right separation if any (prioritize Padding over Margin
            // on both sides).
            // This is a simplified SmallerAdjust() below.
            int pLeft = _padding.Left > 0 ? 1 : 0;
            int mLeft = pLeft == 0 && _margin.Left > 0 ? 1 : 0;
            int pRight = _padding.Right > 0 ? 1 : 0;
            int mRight = pRight == 0 && _margin.Right > 0 ? 1 : 0;

            var padding = new Filler( _padding.Top, pLeft, _padding.Bottom, pRight );
            var margin = new Filler( _margin.Top, mLeft, _margin.Bottom, mRight );

            return new ContentBox( this, _content.SetWidth( 0, false ), padding, margin, _align, _style );
        }
        var ourSpace = _margin.Width + _padding.Width;
        var contentWidth = width - ourSpace;
        var newContent = _content.SetWidth( contentWidth, false );
        delta = width - (ourSpace + newContent.Width);
        if( delta == 0 )
        {
            return new ContentBox( this, newContent, _padding, _margin, _align, _style );
        }
        if( delta > 0 )
        {
            return BiggerAdjust( delta, newContent );
        }
        // Most complex case.
        return SmallerAdjust( ourSpace + delta, newContent );
    }

    ContentBox SmallerAdjust( int delta, IRenderable newContent )
    {
        // First, try to satisfy the padding.
        int padW = Math.Min( delta, _padding.Width );
        // Then gives the remainder to the margin.
        delta -= padW;
        Throw.DebugAssert( "We are in our space.", delta == 0 || delta < _margin.Width );
        int marW = delta > 0 ? delta : 0;

        bool mirror = _align.IsRight();
        return new ContentBox( this,
                                newContent,
                                Distribute( padW, _padding, false, false ),
                                Distribute( marW, _margin, false, _padding.Left > 0 ),
                                _align,
                                _style );

        static Filler Distribute( int w, Filler f, bool mirror, bool paddingHadThreshold )
        {
            Throw.DebugAssert( w <= f.Width );
            var (top, left, bottom, right) = f;

            int addL = w >> 1;
            int addR = addL + (w & 1);
            if( paddingHadThreshold && addL == 1 )
            {
                addL = 0;
                addR++;
            }
            if( mirror ) (addL,addR) = (addR, addL);
            if( addL > left )
            {
                right = w - left;
            }
            else if( addR > right )
            {
                left = w - right;
            }
            else
            {
                left = addL;
                right = addR;
            }

            f = new Filler( top, left, bottom, right );
            return f;
        }
    }

    ContentBox BiggerAdjust( int delta, IRenderable nc )
    {
        Filler padding = _padding;
        Filler margin = _align.IsRight()
                        ? new Filler( _margin.Top, _margin.Left + delta, _margin.Bottom, _margin.Right )
                        : _align.IsCenter()
                            ? new Filler( _margin.Top, _margin.Left + (delta >> 1),
                                          _margin.Bottom, _margin.Right + (delta >> 1) + (delta & 1) )
                            : new Filler( _margin.Top, _margin.Left, _margin.Bottom, _margin.Right + delta );
        return new ContentBox( this, nc, padding, margin, _align, _style );
    }

    /// <summary>
    /// Returns a ContentBox with an updated content.
    /// </summary>
    /// <param name="content">The new content.</param>
    /// <returns>The updated ContentBox.</returns>
    public ContentBox WithContent( IRenderable content ) => content == _content
                                                                ? this
                                                                : _origin == null
                                                                    ? new ContentBox( null, content, _padding, _margin, _align, _style )
                                                                    : new ContentBox( _origin.WithContent( content ), content, _padding, _margin, _align, _style );

    /// <summary>
    /// Adds the provided padding values to the <see cref="Padding"/> and returns a new Box.
    /// </summary>
    /// <param name="top">The top value to add (can be negative).</param>
    /// <param name="left">The left value to add (can be negative).</param>
    /// <param name="bottom">The bottom value to add (can be negative).</param>
    /// <param name="right">The right value to add (can be negative).</param>
    /// <returns>A new ContentBox with the updated padding.</returns>
    public ContentBox AddPadding( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        if( top == 0 && left == 0 && bottom == 0 && right == 0 ) return this;
        if( _origin != null ) return _origin.AddPadding( top, left, bottom, right )
                                            .SetWidth( Width, true );

        int vLeft = int.Clamp( left + _padding.Left, 0, short.MaxValue );
        int vRight = int.Clamp( right + _padding.Right, 0, short.MaxValue );
        var p = new Filler( (short)int.Clamp( top + _padding.Top, 0, short.MaxValue ),
                            (short)vLeft,
                            (short)int.Clamp( bottom + _padding.Bottom, 0, short.MaxValue ),
                            (short)vRight );
        return new ContentBox( null, _content, p, _margin, _align, _style );
    }

    /// <summary>
    /// Adds the provided margin values to the <see cref="Margin"/> and returns a new Box.
    /// </summary>
    /// <param name="top">The top value to add (can be negative).</param>
    /// <param name="left">The left value to add (can be negative).</param>
    /// <param name="bottom">The bottom value to add (can be negative).</param>
    /// <param name="right">The right value to add (can be negative).</param>
    /// <returns>A new ContentBox with the updated margin.</returns>
    public ContentBox AddMargin( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        if( top == 0 && left == 0 && bottom == 0 && right == 0 ) return this;
        if( _origin != null ) return _origin.AddMargin( top, left, bottom, right )
                                       .SetWidth( Width, true );
        int vLeft = int.Clamp( left + _margin.Left, 0, short.MaxValue );
        int vRight = int.Clamp( right + _margin.Right, 0, short.MaxValue );
        var m = new Filler( (short)int.Clamp( top + _margin.Top, 0, short.MaxValue ),
                            (short)vLeft,
                            (short)int.Clamp( bottom + _margin.Bottom, 0, short.MaxValue ),
                            (short)vRight );
        return new ContentBox( null, _content, _padding, m, _align, _style );
    }

    /// <summary>
    /// Returns this or a new box with the specified <see cref="Style"/>.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <returns>A new box or this.</returns>
    public ContentBox WithStyle( TextStyle style ) => _style == style
                                                        ? this
                                                        : _origin == null
                                                            ? new ContentBox( null, _content, _padding, _margin, _align, style )
                                                            : new ContentBox( _origin.WithStyle( style ), _content, _padding, _margin, _align, style );

    /// <summary>
    /// Returns this or a new box with the specified <see cref="Align"/>.
    /// <see cref="ContentAlign.Unknwon"/> is ignored.
    /// </summary>
    /// <param name="align">The alignment.</param>
    /// <returns>A new box or this.</returns>
    public ContentBox WithAlign( ContentAlign align )
    {
        return align == ContentAlign.Unknwon || _align == align
            ? this
            : _origin == null
                ? new ContentBox( null, _content, _padding, _margin, align, _style )
                : _origin.WithAlign( align ).SetWidth( Width, true );
    }

    /// <inheritdoc />
    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

}


