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
    /// Gets the nominal width of this renderable.
    /// This is the unconstrained, "natural" width.
    /// <para>
    /// "Natural" doesn't necessarily means "ideal": a very long
    /// line of text can be more readable when rendered on multiple
    /// lines.
    /// </para>
    /// </summary>
    int NominalWidth { get; }

    /// <summary>
    /// Sets this renderable width.
    /// </summary>
    /// <param name="width">The width to set.</param>
    /// <param name="force">True to expand width greater than th <see cref="NominalWidth"/>.</param>
    /// <returns>The renderable (may be this one).</returns>
    IRenderable SetWidth( int width );

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
