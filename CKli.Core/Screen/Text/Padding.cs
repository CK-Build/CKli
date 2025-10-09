using System.Numerics;

namespace CKli.Core;

public readonly struct Padding : IAdditionOperators<Padding,Padding,Padding>
{
    public readonly byte Top;
    public readonly byte Left;
    public readonly byte Bottom;
    public readonly byte Right;

    public Padding( byte top, byte left, byte bottom, byte right )
    {
        Top = top;
        Left = left;
        Bottom = bottom;
        Right = right;
    }

    public static Padding operator +( Padding left, Padding right )
    {
        int t = left.Top + right.Top;
        int l = left.Left + right.Left;
        int b = left.Bottom + right.Bottom;
        int r = left.Right + right.Right;
        return new Padding( (byte)int.Clamp( t, 0, 255 ), (byte)int.Clamp( l, 0, 255 ), (byte)int.Clamp( b, 0, 255 ), (byte)int.Clamp( r, 0, 255 ) );
    }
}
