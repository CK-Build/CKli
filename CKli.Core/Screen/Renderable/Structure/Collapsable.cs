using CK.Core;

namespace CKli.Core;

public sealed class Collapsable : IRenderable
{
    readonly IRenderable _content;
    readonly TextStyle _style;

    public Collapsable( IRenderable content, TextStyle style = default )
    {
        _content = content;
        _style = style;
    }

    public ScreenType ScreenType => _content.ScreenType;

    public int Height => _content.Height;

    public int Width => 2 + _content.Width;

    public int MinWidth => 2 + _content.MinWidth;

    public int NominalWidth => 2 + _content.NominalWidth;

    public IRenderable Content => _content;

    public TextStyle Style => _style;

    public IRenderable SetWidth( int width, bool allowWider )
    {
        var newContent = _content.SetWidth( width - 2, allowWider );
        return newContent == _content ? this : new Collapsable( newContent, _style );
    }

    public Collapsable WithStyle( TextStyle style ) => new Collapsable( _content, style );

    public Collapsable WithContent( IRenderable content ) => new Collapsable( content, _style );

    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

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
