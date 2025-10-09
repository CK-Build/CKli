using System;
using System.Buffers;

namespace CKli.Core;

public abstract class BlockBase : ILineRenderable
{
    public abstract int Height { get; }
    public abstract int Width { get; }

    public int RenderLine<TArg>( int i, TArg arg, ReadOnlySpanAction<char, TArg> render )
    {
        var t = LineAt(i);
        render( t, arg );
        return t.Length;
    }

    public abstract ReadOnlySpan<char> LineAt( int i );
}
