using System.Diagnostics;
using System.Numerics;

namespace CKli.Core;

/// <summary>
/// A Filler applies to <see cref="ContentBox.Padding"/> and <see cref="ContentBox.Margin"/>.
/// </summary>
[DebuggerDisplay( "{Top},{Left},{Bottom},{Right}" )]
public readonly struct Filler : IAdditionOperators<Filler,Filler,Filler>
{
    public readonly short Top;
    public readonly short Left;
    public readonly short Bottom;
    public readonly short Right;
    public readonly int Width => Left + Right;
    public readonly int Height => Top + Bottom;

    public Filler( (int Top, int Left, int Bottom, int Right) value )
        : this( value.Top, value.Left, value.Bottom, value.Right )
    {
    }

    public Filler( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        Top = (short)int.Clamp( top, 0, short.MaxValue );
        Left = (short)int.Clamp( left, 0, short.MaxValue );
        Bottom = (short)int.Clamp( bottom, 0, short.MaxValue );
        Right = (short)int.Clamp( right, 0, short.MaxValue );
    }

    public Filler( short top, short left, short bottom, short right )
    {
        Top = top;
        Left = left;
        Bottom = bottom;
        Right = right;
    }

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
}
