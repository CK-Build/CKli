using System;

namespace CKli.Core;

/// <summary>
/// Target to render <see cref="IRenderable"/>.
/// </summary>
public interface IRenderTarget
{
    /// <summary>
    /// Writes the text in provided style if possible.
    /// </summary>
    /// <param name="text">The text to render.</param>
    /// <param name="style">The style to apply.</param>
    void Write( ReadOnlySpan<char> text, TextStyle style );

    /// <summary>
    /// Starts a write session: this enables buffering when possible.
    /// </summary>
    void BeginUpdate();

    /// <summary>
    /// Ends a write session.
    /// </summary>
    void EndUpdate();

    /// <summary>
    /// Must write the end of the line.
    /// </summary>
    /// <param name="newLine">False to not start a new line.</param>
    /// <param name="fill">True to fill the rest of the line with the current background color.</param>
    void EndOfLine( bool newLine = true, bool fill = true );

    /// <summary>
    /// Gets the screen type.
    /// </summary>
    ScreenType ScreenType { get; }
}
