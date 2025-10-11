using System;

namespace CKli.Core;

public readonly struct Color : IEquatable<Color>
{
    readonly byte _color;

    /// <summary>
    /// The default is white on black.
    /// </summary>
    public static readonly Color Default = new Color( ConsoleColor.White, ConsoleColor.Black );

    public Color( ConsoleColor foreColor, ConsoleColor backColor )
    {
        _color = (byte)((int)foreColor | ((int)backColor << 4));
    }

    public ConsoleColor ForeColor => (ConsoleColor)(_color & 0x0F);
    public ConsoleColor BackColor => (ConsoleColor)(_color >> 4);

    public Color Invert() => new Color( BackColor, ForeColor );

    public bool Equals( Color other ) => _color == other._color;

    public override bool Equals( object? obj ) => obj is Color c && Equals( c );
    public override int GetHashCode() => HashCode.Combine( _color );

    public static bool operator ==( Color left, Color right ) => left.Equals( right );
    public static bool operator !=( Color left, Color right ) => !(left == right);
}
