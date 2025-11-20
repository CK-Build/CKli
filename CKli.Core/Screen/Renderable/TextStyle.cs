using System;
using System.Diagnostics;

namespace CKli.Core;

/// <summary>
/// Defines text style.
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public readonly struct TextStyle : IEquatable<TextStyle>
{
    const int _colorBit = 1 << 7;
    readonly Color _color;
    readonly byte _effect;

    /// <summary>
    /// None TextStyle is the <c>default</c>, it has no impact: <see cref="IgnoreColor"/> and <see cref="IgnoreEffect"/> are both true.
    /// </summary>
    public static readonly TextStyle None = default;

    /// <summary>
    /// Default is gray regular text on black background.
    /// </summary>
    public static readonly TextStyle Default = new TextStyle( ConsoleColor.Gray, ConsoleColor.Black, TextEffect.Regular );

    /// <summary>
    /// Initializes a new <see cref="TextStyle"/>.
    /// </summary>
    /// <param name="color">Foreground and background text color.</param>
    /// <param name="effect">Optional text effect.</param>
    public TextStyle( Color color, TextEffect effect = TextEffect.Ignore )
    {
        _color = (effect & TextEffect.Invert) != 0 ? color.Invert() : color;
        _effect = (byte)((int)effect | _colorBit);
    }

    /// <summary>
    /// Initializes a new <see cref="TextStyle"/>.
    /// </summary>
    /// <param name="foreColor">The foreground text color.</param>
    /// <param name="backColor">The background text color.</param>
    /// <param name="effect">The text effect.</param>
    public TextStyle( ConsoleColor foreColor, ConsoleColor backColor = ConsoleColor.Black, TextEffect effect = TextEffect.Ignore )
        : this( new Color( foreColor, backColor ), effect )
    {
    }

    /// <summary>
    /// Initializes a new <see cref="TextStyle"/> without color (<see cref="IgnoreColor"/> is true).
    /// </summary>
    /// <param name="effect">The text effect.</param>
    public TextStyle( TextEffect effect )
    {
        _effect = (byte)effect;
    }

    TextStyle( int effect, Color color )
    {
        _color = (effect & (int)TextEffect.Invert) == 0 ? color : color.Invert();
        _effect = (byte)effect;
    }

    /// <summary>
    /// Gets whether this style has no color: final color is determined a upper container. 
    /// </summary>
    public bool IgnoreColor => (_effect & _colorBit) == 0;

    /// <summary>
    /// Gets whether this style has no effect: final effect is determined a upper container. 
    /// </summary>
    public bool IgnoreEffect => (_effect & 0x7F) == 0;

    /// <summary>
    /// Gets whether this style is <see cref="None"/>. 
    /// </summary>
    public bool IgnoreAll => _effect == 0;

    /// <summary>
    /// Gets whether this style has a color and an effect. 
    /// </summary>
    public bool IgnoreNothing => !IgnoreColor && !IgnoreEffect;

    /// <summary>
    /// Gets the color.
    /// </summary>
    public Color Color => _color;

    /// <summary>
    /// Gets the effect.
    /// </summary>
    public TextEffect Effect => (TextEffect)(_effect & 0x7F);

    /// <summary>
    /// Gets the <see cref="Color"/>.<see cref="Color.Invert">Invert()</see> color if <see cref="IsInvert"/> is true.
    /// </summary>
    public Color NonInvertedColor => IsInvert ? _color.Invert() : _color;

    /// <summary>
    /// Gets whether this <see cref="Effect"/> inverts the Color.
    /// </summary>
    bool IsInvert => ((TextEffect)_effect & TextEffect.Invert) != 0;

    /// <summary>
    /// Returns a style with the new effect.
    /// </summary>
    /// <param name="effect">The effect to apply. <see cref="TextEffect.Ignore"/> has no effect: this is returned.</param>
    /// <returns>The new text style.</returns>
    public TextStyle With( TextEffect effect ) => effect == TextEffect.Ignore
                                                    ? this
                                                    : new TextStyle( (int)effect | (_effect & 0x80), NonInvertedColor );

    /// <summary>
    /// Returns a style with the color.
    /// </summary>
    /// <param name="color">The new color to consider.</param>
    /// <param name="ignoreColor">Whether the color must be ignored or not.</param>
    /// <returns>The new text style.</returns>
    public TextStyle With( Color color, bool? ignoreColor ) => new TextStyle( ignoreColor switch {
                                                                                    true => _effect & ~_colorBit,
                                                                                    false => _effect | _colorBit,
                                                                                    _ => _effect
                                                                                },
                                                                                color );

    /// <summary>
    /// Returns a style that combines this one with another one: the <paramref name="other"/> style
    /// color and/or effect apply if they are not ignored. 
    /// </summary>
    /// <param name="other">The other style.</param>
    /// <returns>The new text style.</returns>
    public TextStyle OverrideWith( TextStyle other ) => other.IgnoreColor
                                                        ? With( other.Effect )
                                                        : other.IgnoreEffect
                                                            ? With( other.Color, false )
                                                            : other;

    /// <summary>
    /// Returns a style that uses the <paramref name="other"/> one if this <see cref="IgnoreColor"/>
    /// and/or <see cref="IgnoreEffect"/> is true.
    /// </summary>
    /// <param name="other">The other style.</param>
    /// <returns>The new text style.</returns>
    public TextStyle CompleteWith( TextStyle other ) => IgnoreColor
                                                        ? (IgnoreEffect
                                                            ? other
                                                            : With( other.NonInvertedColor, other.IgnoreColor ))
                                                        : IgnoreEffect
                                                            ? With( other.Effect )
                                                            : other;

    /// <inheritdoc />
    public bool Equals( TextStyle other ) => _color == other._color && _effect == other._effect;

    /// <inheritdoc />
    public override bool Equals( object? obj ) => obj is TextStyle s && Equals( s );

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine( _color, _effect );

    /// <summary>
    /// Overridden to return the color and effect.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{(IgnoreColor ? "?" : Color.ToString())}-{(IgnoreEffect ? "?" : Effect)}";

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public static bool operator ==( TextStyle left, TextStyle right ) => left.Equals( right );

    public static bool operator !=( TextStyle left, TextStyle right ) => !(left == right);
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member


}
