using CK.Core;
using System;

namespace CKli.Core;

/// <summary>
/// Defines a column in a <see cref="TableLayout"/>.
/// </summary>
public sealed class ColumnDefinition
{
    readonly int _minWidth;
    readonly int _maxWidth;
    readonly int _index;
    readonly IRenderable? _header;

    /// <summary>
    /// Initializes a new column definition.
    /// </summary>
    /// <param name="index">The column index.</param>
    /// <param name="header">Optional column header (not implemented yet).</param>
    /// <param name="minWidth">The minimal with of the column.</param>
    /// <param name="maxWidth">The maximal with of the column.</param>
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

    /// <summary>
    /// Gets the column index.
    /// </summary>
    public int Index => _index;

    /// <summary>
    /// Gets the column minimal width.
    /// </summary>
    public int MinWidth => _minWidth;

    /// <summary>
    /// Gets the column maximal width.
    /// </summary>
    public int MaxWidth => _maxWidth;

    /// <summary>
    /// Gets the header (not implemented yet).
    /// </summary>
    public IRenderable? Header => _header;
}
