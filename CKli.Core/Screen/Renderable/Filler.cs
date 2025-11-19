using System.Diagnostics;
using System.Numerics;

namespace CKli.Core;

/// <summary>
/// A Filler applies to <see cref="ContentBox.Padding"/> and <see cref="ContentBox.Margin"/>.
/// </summary>
[DebuggerDisplay( "{Top},{Left},{Bottom},{Right}" )]
public readonly struct Filler : IAdditionOperators<Filler,Filler,Filler>
{
    /// <summary>
    /// The top value.
    /// </summary>
    public readonly short Top;

    /// <summary>
    /// The left value.
    /// </summary>
    public readonly short Left;

    /// <summary>
    /// The bottom value.
    /// </summary>
    public readonly short Bottom;

    /// <summary>
    /// The right value.
    /// </summary>
    public readonly short Right;

    /// <summary>
    /// Gets the total width (Left + Right).
    /// </summary>
    public readonly int Width => Left + Right;

    /// <summary>
    /// Gets the total height (Top + Bottom).
    /// </summary>
    public readonly int Height => Top + Bottom;

    /// <summary>
    /// Initializes a new Filler.
    /// </summary>
    /// <param name="value">The top, left, bottom, right value tuple.</param>
    public Filler( (int Top, int Left, int Bottom, int Right) value )
        : this( value.Top, value.Left, value.Bottom, value.Right )
    {
    }

    /// <summary>
    /// Initializes a new Filler.
    /// </summary>
    /// <param name="top">The top value.</param>
    /// <param name="left">The left value.</param>
    /// <param name="bottom">The bottom value.</param>
    /// <param name="right">The right value.</param>
    public Filler( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        Top = (short)int.Clamp( top, 0, short.MaxValue );
        Left = (short)int.Clamp( left, 0, short.MaxValue );
        Bottom = (short)int.Clamp( bottom, 0, short.MaxValue );
        Right = (short)int.Clamp( right, 0, short.MaxValue );
    }

    /// <summary>
    /// Initializes a new Filler.
    /// </summary>
    /// <param name="top">The top value.</param>
    /// <param name="left">The left value.</param>
    /// <param name="bottom">The bottom value.</param>
    /// <param name="right">The right value.</param>
    public Filler( short top, short left, short bottom, short right )
    {
        Top = top;
        Left = left;
        Bottom = bottom;
        Right = right;
    }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

    public static Filler operator +( Filler left, Filler right )
    {
        int t = left.Top + right.Top;
        int l = left.Left + right.Left;
        int b = left.Bottom + right.Bottom;
        int r = left.Right + right.Right;
        return new Filler( t, l, b, r );
    }

    public void Deconstruct( out int top, out int left, out int bottom, out int right )
    {
        top = Top;
        left = Left;
        bottom = Bottom;
        right = Right;
    }

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
