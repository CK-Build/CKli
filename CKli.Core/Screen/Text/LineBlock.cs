using CK.Core;
using System;
using System.Collections.Immutable;

namespace CKli.Core;

public sealed class LineBlock : BlockBase
{
    readonly string _string;
    readonly int _witdh;

    public LineBlock( string text )
    {
        _string = text;
        var sText = text.AsSpan().TrimEnd();
        Throw.CheckArgument( !sText.Contains( '\n' ) );
        _witdh = sText.Length;
    }

    public override int Height => 1;

    public override int Width => _witdh;

    public override ReadOnlySpan<char> LineAt( int i ) =>  i == 0 ? _string.AsSpan( 0, _witdh ) : default;

    public override string ToString() => _string;
}
