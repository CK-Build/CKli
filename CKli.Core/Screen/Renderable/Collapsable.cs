using System;

namespace CKli.Core;

public sealed class Collapsable : IRenderable
{
    readonly IRenderable _content;

    public Collapsable( IRenderable content )
    {
        _content = content;
    }

    public int Height => _content.Height;

    public int Width => 1;

    public SegmentRenderer CollectRenderer( int line, SegmentRenderer previous )
    {
        throw new NotImplementedException();
    }


}


