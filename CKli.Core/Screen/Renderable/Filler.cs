using System.Numerics;

namespace CKli.Core;

/// <summary>
/// A Filler applies to <see cref="ContentBox.Padding"/> and <see cref="ContentBox.Margin"/>.
/// </summary>
public readonly struct Filler : IAdditionOperators<Filler,Filler,Filler>
{
    public readonly byte Top;
    public readonly byte Left;
    public readonly byte Bottom;
    public readonly byte Right;

    public Filler( int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        Top = (byte)int.Clamp( top, 0, 255 );
        Left = (byte)int.Clamp( left, 0, 255 );
        Bottom = (byte)int.Clamp( bottom, 0, 255 );
        Right = (byte)int.Clamp( right, 0, 255 );
    }

    public Filler( byte top, byte left, byte bottom, byte right )
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
}
