using CK.Core;
using System;
using System.Collections.Immutable;

namespace CKli.Core;

public sealed class TextBlock : BlockBase
{
    readonly string _string;
    readonly ImmutableArray<(int,int)> _lines;
    readonly int _witdh;

    public TextBlock( string text )
    {
        _string = text;
        var sText = text.AsSpan().TrimEnd();
        var lineCount = sText.Count( '\n' ) + 1;
        if( lineCount == 1 )
        {
            _lines = [( 0, sText.Length )];
            _witdh = sText.Length;
        }
        else
        {
            _witdh = 0;
            var b = ImmutableArray.CreateBuilder<(int,int)>( lineCount );
            int start = 0;
            int idx = sText.IndexOf( "\n" );
            do
            {
                int len = idx > 0 && sText[idx - 1] == '\r' ? idx - 1 : idx;
                if( len > _witdh ) _witdh = len;
                b.Add( ( start, len ) );
                start += ++idx;
                sText = sText.Slice( idx );
                idx = sText.IndexOf( "\n" );
            }
            while( idx >= 0 );
            b.Add( ( start, sText.Length ) );
            _lines = b.MoveToImmutable();
        }
    }

    public override int Height => _lines.Length;

    public override int Width => _witdh;

    public override ReadOnlySpan<char> LineAt( int i )
    {
        if( i >= 0 && i < _lines.Length )
        {
            var (start, len) = _lines[i];
            return _string.AsSpan( start, len );
        }
        return default;
    }

    public override string ToString() => _string;
}
