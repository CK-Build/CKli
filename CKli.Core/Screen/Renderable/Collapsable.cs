using CK.Core;
using LibGit2Sharp;
using System;

namespace CKli.Core;

public sealed class Collapsable : IRenderable
{
    readonly IRenderable _content;
    readonly TextStyle _style;

    public Collapsable( IRenderable content )
        : this( content, new TextStyle( TextEffect.Invert ) )
    {
    }

    public Collapsable( IRenderable content, TextStyle style = default )
    {
        _content = content;
        _style = style;
    }

    public int Height => _content.Height;

    public int Width => 2 + _content.Width;

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
            Target.Append( "> ", style );
            RenderContent();
        }
    }

    sealed class BodyLineRenderer( SegmentRenderer parent, int length, IRenderable content, int line, int actualHeight, TextStyle style )
        : SegmentRenderer( parent, length, content, line, actualHeight )
    {
        protected override void Render()
        {
            Target.Append( "│ ", style );
            RenderContent();
        }
    }
}


