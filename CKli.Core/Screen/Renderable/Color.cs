using System;
using System.Diagnostics;

namespace CKli.Core;

/// <summary>
/// Combines a <see cref="ForeColor"/> and a <see cref="BackColor"/> into one byte.
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public readonly struct Color : IEquatable<Color>
{
    readonly byte _color;

    /// <summary>
    /// The default is white on black.
    /// </summary>
    public static readonly Color Default = new Color( ConsoleColor.White, ConsoleColor.Black );

    /// <summary>
    /// Initializes a new color.
    /// </summary>
    /// <param name="foreColor">The foreground color.</param>
    /// <param name="backColor">The background color.</param>
    public Color( ConsoleColor foreColor, ConsoleColor backColor )
    {
        _color = (byte)((int)foreColor | ((int)backColor << 4));
    }

    /// <summary>
    /// Gets the foreground color.
    /// </summary>
    public ConsoleColor ForeColor => (ConsoleColor)(_color & 0x0F);

    /// <summary>
    /// Gets the background color.
    /// </summary>
    public ConsoleColor BackColor => (ConsoleColor)(_color >> 4);

    /// <summary>
    /// Returns an inverted backgroung/foreground color.
    /// </summary>
    /// <returns></returns>
    public Color Invert() => new Color( BackColor, ForeColor );

    /// <inheritdoc />
    public bool Equals( Color other ) => _color == other._color;

    /// <inheritdoc />
    public override bool Equals( object? obj ) => obj is Color c && Equals( c );

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine( _color );

    /// <summary>
    /// Overridden to return "<see cref="ForeColor"/>/<see cref="BackColor"/>".
    /// </summary>
    /// <returns>The fore/back color.</returns>
    public override string ToString() => $"{ForeColor}/{BackColor}";

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public static bool operator ==( Color left, Color right ) => left.Equals( right );
    public static bool operator !=( Color left, Color right ) => !(left == right);
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

}
