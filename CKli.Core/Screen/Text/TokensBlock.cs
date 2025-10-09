using System;
using System.Collections.Immutable;

namespace CKli.Core;

public sealed class TokensBlock : BlockBase
{
    readonly string _string;
    readonly ImmutableArray<string> _tokens;

    public TokensBlock( ImmutableArray<string> tokens, string separator )
    {
        _string = string.Join( separator, tokens );
        _tokens = tokens;
    }

    public override int Height => 1;

    public override int Width => _string.Length;

    public override ReadOnlySpan<char> LineAt( int i ) => i == 0 ? _string.AsSpan() : default;

    public override string ToString() => _string;
}
