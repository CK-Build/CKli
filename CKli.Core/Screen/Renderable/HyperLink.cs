using CK.Core;
using System;

namespace CKli.Core;

public sealed class HyperLink : IRenderable
{
    readonly IRenderable _content;
    readonly Uri _target;

    public HyperLink( IRenderable content, Uri target )
    {
        _content = content;
        _target = target;
    }

    public ScreenType ScreenType => _content.ScreenType;

    public int Height => _content.Height;

    public int Width => _content.Width;

    public IRenderable Content => _content;

    public Uri Target => _target;

    public HyperLink WithTarget( Uri target ) => new HyperLink( _content, target );

    public HyperLink WithContent( IRenderable content ) => new HyperLink( content, _target );

    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

    public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight )
    {
        Throw.CheckArgument( line >= 0 && line < actualHeight && actualHeight >= Height );
        if( line < actualHeight )
        {
            if( ScreenType.HasAnsiLink )
            {
                _ = new HyperLinkRenderer( parent, Width, _content, line, actualHeight, _target );
            }
            else
            {
                _ = new SegmentRenderer( parent, Width, _content, line, actualHeight );
            }
        }
    }

    sealed class HyperLinkRenderer( SegmentRenderer parent, int length, IRenderable content, int line, int actualHeight, Uri target )
        : SegmentRenderer( parent, length, content, line, actualHeight )
    {
        protected override void Render()
        {
            Target.Write( AnsiCodes.HyperLinkPrefix, default );
            Target.Write( target.ToString(), default );
            Target.Write( AnsiCodes.HyperLinkInfix, default );
            RenderContent();
            Target.Write( AnsiCodes.HyperLinkSuffix, default );
        }
    }
}
