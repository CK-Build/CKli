using System;
using System.Collections.Generic;

namespace CKli.Core;

public interface IRenderable
{
    int Height { get; }

    int Width { get; }

    SegmentRenderer CollectRenderer( int line, SegmentRenderer previous );

    public static readonly IRenderable None = new RenderableUnit();

    private sealed class RenderableUnit : IRenderable
    {
        public int Height => 0;

        public int Width => 0;

        public SegmentRenderer CollectRenderer( int line, SegmentRenderer previous ) => previous;
    }

}
