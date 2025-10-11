using System;

namespace CKli.Core;

/// <summary>
/// Basic effects (not guaranteed to work on any environment).
/// </summary>
[Flags]
public enum TextEffect : byte
{
    /// <summary>
    /// No effect specification. Current effect (whatever it is) applies. 
    /// </summary>
    Ignore = 0,

    /// <summary>
    /// Regular text (no effect).
    /// </summary>
    Regular = 1 << 0,

    /// <summary>
    /// Swaps the foreground and background colors.
    /// </summary>
    Invert = 1 << 1,

    /// <summary>
    /// Bold text.
    /// </summary>
    Bold = 1 << 2,

    /// <summary>
    /// Italic text.
    /// </summary>
    Italic = 1 << 3,

    /// <summary>
    /// Underlined text.
    /// </summary>
    Underline = 1 << 4,

    /// <summary>
    /// Makes text blink.
    /// </summary>
    Blink = 1 << 5,

    /// <summary>
    /// Shows text with a horizontal line through the center.
    /// </summary>
    Strikethrough = 1 << 6,
}
