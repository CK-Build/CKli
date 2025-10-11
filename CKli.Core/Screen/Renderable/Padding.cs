using System.Numerics;

namespace CKli.Core;

public readonly struct Padding : IAdditionOperators<Padding,Padding,Padding>
{
    public readonly byte Top;
    public readonly byte Left;
    public readonly byte Bottom;
    public readonly byte Right;

    public Padding( int top, int left, int bottom, int right )
    {
        Top = (byte)int.Clamp( top, 0, 255 );
        Left = (byte)int.Clamp( left, 0, 255 );
        Bottom = (byte)int.Clamp( bottom, 0, 255 );
        Right = (byte)int.Clamp( right, 0, 255 );
    }

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
        return new Padding( t, l, b, r );
    }
}
