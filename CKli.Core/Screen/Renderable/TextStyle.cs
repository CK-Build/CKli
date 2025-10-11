using System;

namespace CKli.Core;

public readonly struct TextStyle : IEquatable<TextStyle>
{
    const int _colorBit = 1 << 7;
    readonly Color _color;
    readonly byte _effect;

    /// <summary>
    /// None TextStyle is the <c>default</c>, it has no impact: <see cref="IgnoreColors"/> and <see cref="IgnoreEffect"/> are both true.
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

    public bool IgnoreColors => (_effect & _colorBit) == 0;
    public bool IgnoreEffect => (_effect & 0x7F) == 0;
    public Color Color => _color;
    public TextEffect Effect => (TextEffect)(_effect & 0x7F);

    public TextStyle With( TextEffect effect ) => effect == TextEffect.Ignore
                                                    ? this
                                                    : new TextStyle( (effect & TextEffect.Invert) == 0 ? _color : _color.Invert(), effect );

    public TextStyle With( Color color ) => new TextStyle( _effect, color );

    public bool Equals( TextStyle other ) => _color == other._color && _effect == other._effect;

    public override bool Equals( object? obj ) => obj is TextStyle s && Equals( s );

    public override int GetHashCode() => HashCode.Combine( _color, _effect );

    public static bool operator ==( TextStyle left, TextStyle right ) => left.Equals( right );

    public static bool operator !=( TextStyle left, TextStyle right ) => !(left == right);
}
