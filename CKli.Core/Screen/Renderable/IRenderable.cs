namespace CKli.Core;

/// <summary>
/// Fundamental renderable abstraction.
/// </summary>
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
    /// <param name="allowWider">
    /// True to allow the width to be greater than the <see cref="NominalWidth"/>.
    /// When false, the width is at most the NominalWidth (for <see cref="TextBlock"/>, this
    /// is always the case).
    /// </param>
    /// <returns>The renderable (may be this one).</returns>
    IRenderable SetWidth( int width, bool allowWider = true );

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
