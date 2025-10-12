using CK.Core;
using CK.Monitoring;
using System;

namespace CKli.Core;

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
    /// Default is white regular text on black background.
    /// </summary>
    public static readonly TextStyle Default = new TextStyle( ConsoleColor.White, ConsoleColor.Black, TextEffect.Regular );

    public TextStyle( Color color, TextEffect effect = TextEffect.Ignore )
    {
        _color = (effect & TextEffect.Invert) != 0 ? color.Invert() : color;
        _effect = (byte)((int)effect | _colorBit);
    }

    public TextStyle( ConsoleColor foreColor, ConsoleColor backColor, TextEffect effect = TextEffect.Ignore )
        : this( new Color( foreColor, backColor ), effect )
    {
    }

    public TextStyle( TextEffect effect )
    {
        _effect = (byte)effect;
    }

    TextStyle( int effect, Color color )
    {
        _color = (effect & (int)TextEffect.Invert) == 0 ? color : color.Invert();
        _effect = (byte)effect;
    }

    public bool IgnoreColor => (_effect & _colorBit) == 0;
    public bool IgnoreEffect => (_effect & 0x7F) == 0;
    public bool IgnoreAll => _effect == 0;
    public bool IgnoreNothing => !IgnoreColor && !IgnoreEffect;

    public Color Color => _color;
    public TextEffect Effect => (TextEffect)(_effect & 0x7F);
    public Color NonInvertedColor => IsInvert ? _color.Invert() : _color;

    bool IsInvert => ((TextEffect)_effect & TextEffect.Invert) != 0;

    public TextStyle With( TextEffect effect ) => effect == TextEffect.Ignore
                                                    ? this
                                                    : new TextStyle( (int)effect | (_effect & 0x80), NonInvertedColor );

    public TextStyle With( Color color, bool? ignoreColor ) => new TextStyle( ignoreColor switch {
                                                                                    true => _effect | _colorBit,
                                                                                    false => _effect & ~_colorBit,
                                                                                    _ => _effect
                                                                                },
                                                                                color );
    public TextStyle OverrideWith( TextStyle other ) => other.IgnoreColor
                                                        ? With( other.Effect )
                                                        : other.IgnoreEffect
                                                            ? With( other.Color, other.IgnoreColor )
                                                            : other;

    public TextStyle CompleteWith( TextStyle other ) => IgnoreColor
                                                        ? (IgnoreEffect
                                                            ? other
                                                            : With( other.NonInvertedColor, other.IgnoreColor ))
                                                        : IgnoreEffect
                                                            ? With( other.Effect )
                                                            : other;

    TextStyle CompleteWithLiftInvert( TextStyle other )
    {
        // Stupid case. May be a Throw.CheckArgument but let's be nice.
        if( other.IgnoreAll ) return this;

        // If we don't exit early with this or other, these are the final results.
        Color finalColor;
        int effect;

        if( IgnoreColor )
        {
            if( IgnoreEffect )
            {
                return other;
            }
            // If the other also ignores its color, we could return this but
            // doing so will forget to combine the invert effect. We can only
            // return this if the other ignores both (done above).
            // With 2 ignored colors we retain the other one (and keep the fact that
            // the color is eventually ignored).
            effect = _effect;
            if( !other.IgnoreColor ) effect |= _colorBit;
            if( IsInvert )
            {
                if( other.IsInvert )
                {
                    Throw.DebugAssert( "If inverting, then the effect applies.", !other.IgnoreEffect );
                    finalColor = other.NonInvertedColor;
                    effect &= ~(int)TextEffect.Invert;
                }
                else
                {
                    finalColor = other.Color.Invert();
                }
            }
            else
            {
                if( other.IsInvert )
                {
                    Throw.DebugAssert( !other.IgnoreEffect );
                    finalColor = other.Color;
                    effect |= (int)TextEffect.Invert;
                }
                else
                {
                    finalColor = other.Color;
                }
            }
        }
        else
        {
            // Our color is set. If we also have the effect, we're done.
            // We are also done if the other ignores its effect.
            if( !IgnoreEffect || other.IgnoreEffect ) return this;

            effect = other._effect | _colorBit;
            finalColor = _color;
            if( other.IsInvert ) finalColor = finalColor.Invert();
        }
        return new TextStyle( effect, finalColor );
    }

    public bool Equals( TextStyle other ) => _color == other._color && _effect == other._effect;

    public override bool Equals( object? obj ) => obj is TextStyle s && Equals( s );

    public override int GetHashCode() => HashCode.Combine( _color, _effect );

    public static bool operator ==( TextStyle left, TextStyle right ) => left.Equals( right );

    public static bool operator !=( TextStyle left, TextStyle right ) => !(left == right);
}
