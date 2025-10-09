using CK.Core;
using System;
using System.Collections.Immutable;

namespace CKli.Core;

public sealed class WordBlock : BlockBase
{
    readonly string _string;
    readonly int _offset;
    readonly int _witdh;

    public WordBlock( string text )
    {
        _string = text;
        var sText = text.AsSpan().Trim();
        var sTrimmed = sText.TrimStart();
        Throw.CheckArgument( !sTrimmed.Contains( '\n' ) );
        _offset = sText.Length - sTrimmed.Length;
        _witdh = sTrimmed.Length;
    }

    public override int Height => 1;

    public override int Width => _witdh;

    public override ReadOnlySpan<char> LineAt( int i ) => i == 0 ? _string.AsSpan( _offset, _witdh ) : default;

    public override string ToString() => _string;
}
