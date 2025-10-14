using System;
using System.Collections.Generic;

namespace CKli.Core;

public interface IRenderable
{
    /// <summary>
    /// Gets the width in characters of this renderable.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the height in characters of this renderable.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Builds the segment renderer for the provided <paramref name="line"/>.
    /// </summary>
    /// <param name="line">The line number: between 0 and <paramref name="actualHeight"/>.</param>
    /// <param name="parent">The parent renderer.</param>
    /// <param name="actualHeight">Height to consider. Always greater than or equal to <see cref="Height"/>.</param>
    void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight );

    public static readonly IRenderable Unit = new RenderableUnit();

    private sealed class RenderableUnit : IRenderable
    {
        public int Height => 0;

        public int Width => 0;

        public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight ) { }
    }

}
