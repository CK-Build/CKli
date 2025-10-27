using CK.Core;
using System;

namespace CKli.Core;

public sealed class ColumnDefinition
{
    readonly int _minWidth;
    readonly int _maxWidth;
    readonly int _index;
    readonly IRenderable? _header;

    public ColumnDefinition( int index = 0, IRenderable? header = null, int minWidth = 0, int maxWidth = 0 )
    {
        Throw.CheckArgument( index >= 0 );
        if( header != null && minWidth < header.MinWidth )
        {
            minWidth = header.MinWidth;
        }
        _minWidth = minWidth > 0 ? minWidth : 0;
        _maxWidth = maxWidth > 0 ? Math.Max( minWidth, maxWidth ) : 0;
        _index = index;
        _header = header;
    }

    public int Index => _index;

    public int MinWidth => _minWidth;

    public IRenderable? Header => _header;

    public int MaxWidth => _maxWidth;
}
