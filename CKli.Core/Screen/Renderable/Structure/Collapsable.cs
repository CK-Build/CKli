using CK.Core;

namespace CKli.Core;

/// <summary>
/// Wraps a renderable in a collapsable left column.
/// </summary>
public sealed class Collapsable : IRenderable
{
    readonly IRenderable _content;
    readonly TextStyle _style;

    /// <summary>
    /// Initializes a new <see cref="Collapsable"/>.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="style">Optional text style to apply to the content.</param>
    public Collapsable( IRenderable content, TextStyle style = default )
    {
        _content = content;
        _style = style;
    }

    /// <inheritdoc />
    public ScreenType ScreenType => _content.ScreenType;

    /// <inheritdoc />
    public int Height => _content.Height;

    /// <inheritdoc />
    public int Width => 2 + _content.Width;

    /// <inheritdoc />
    public int MinWidth => 2 + _content.MinWidth;

    /// <inheritdoc />
    public int NominalWidth => 2 + _content.NominalWidth;

    /// <summary>
    /// Gets the content.
    /// </summary>
    public IRenderable Content => _content;

    /// <summary>
    /// Gets the text style to apply to the content.
    /// </summary>
    public TextStyle Style => _style;

    /// <inheritdoc />
    public IRenderable SetWidth( int width, bool allowWider )
    {
        var newContent = _content.SetWidth( width - 2, allowWider );
        return newContent == _content ? this : new Collapsable( newContent, _style );
    }

    /// <summary>
    /// Returns a Collapsable with the provided text style.
    /// </summary>
    /// <param name="style">The style to apply.</param>
    /// <returns>This or a new collapsable.</returns>
    public Collapsable WithStyle( TextStyle style ) => style == _style ? this : new Collapsable( _content, style );

    /// <summary>
    /// Returns a Collapsable with the provided content.
    /// </summary>
    /// <param name="content">The new content.</param>
    /// <returns>This or a new collapsable.</returns>
    public Collapsable WithContent( IRenderable content ) => content == _content ? this : new Collapsable( content, _style );

    /// <inheritdoc />
    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

    /// <inheritdoc />
    public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight )
    {
        Throw.CheckArgument( line >= 0 && line < actualHeight && actualHeight >= Height );
        if( line < actualHeight )
        {
            if( line == 0 )
            {
                _ = new FirstLineRenderer( parent, Width, _content, line, actualHeight, _style );
            }
            else
            {
                _ = new BodyLineRenderer( parent, Width, _content, line, actualHeight, _style );
            }
        }
    }

    sealed class FirstLineRenderer( SegmentRenderer parent, int length, IRenderable content, int line, int actualHeight, TextStyle style )
        : SegmentRenderer( parent, length, content, line, actualHeight )
    {
        protected override void Render()
        {
            Target.Write( "> ", style );
            RenderContent();
        }
    }

    sealed class BodyLineRenderer( SegmentRenderer parent, int length, IRenderable content, int line, int actualHeight, TextStyle style )
        : SegmentRenderer( parent, length, content, line, actualHeight )
    {
        protected override void Render()
        {
            Target.Write( "│ ", style );
            RenderContent();
        }
    }
}
