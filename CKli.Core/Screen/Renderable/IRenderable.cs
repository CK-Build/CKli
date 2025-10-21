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
    /// Gets the minimal possible width for this renderable.
    /// </summary>
    int MinWidth { get; }

    /// <summary>
    /// Gets the height in characters of this renderable.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Gets the screen type.
    /// </summary>
    ScreenType ScreenType { get; }

    /// <summary>
    /// Builds the segment renderer for the provided <paramref name="line"/>.
    /// </summary>
    /// <param name="line">The line number: between 0 and <paramref name="actualHeight"/>.</param>
    /// <param name="parent">The parent renderer.</param>
    /// <param name="actualHeight">Height to consider. Always greater than or equal to <see cref="Height"/>.</param>
    void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight );

    /// <summary>
    /// Support for visitor pattern.
    /// </summary>
    /// <param name="visitor">The visitor.</param>
    /// <returns>The result of the visit.</returns>
    IRenderable Accept( RenderableVisitor visitor );

}
