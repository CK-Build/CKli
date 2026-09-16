using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// A table layout doesn't handle borders. 
/// </summary>
public partial class TableLayout : IRenderable
{
    readonly IRenderable _rows;
    readonly ColDef[] _cols;
    readonly ImmutableArray<ColumnDefinition> _columns;
    readonly int _minWidth;
    readonly int _nominalWidth;
    readonly int _width;

    TableLayout( IRenderable rows, ColDef[] cols, ImmutableArray<ColumnDefinition> columns, int minWidth, int nominalWidth, int width )
    {
        _rows = rows;
        _cols = cols;
        _columns = columns;
        _minWidth = minWidth;
        _nominalWidth = nominalWidth;
        _width = width;
    }

    /// <summary>
    /// Factory method for a <see cref="TableLayout"/>.
    /// </summary>
    /// <param name="rows">The content rows.</param>
    /// <param name="columns">Optional columns definition.</param>
    /// <returns>A TableLayout or the <paramref name="rows"/> if no columns can be inferred.</returns>
    public static IRenderable Create( IRenderable rows, params ImmutableArray<ColumnDefinition> columns )
    {
        var cols = new List<ColDef>();
        if( !columns.IsDefaultOrEmpty )
        {
            foreach( var d in columns )
            {
                if( d.Index < cols.Count )
                {
                    Throw.ArgumentException( nameof( columns ), $"Invalid column Index = {d.Index}. Must be at least {cols.Count}." );
                }
                while( d.Index > cols.Count )
                {
                    cols.Add( new ColDef() );
                }
                cols.Add( new ColDef( d ) );
            }
        }
        bool hasColumns = rows is VerticalContent v
                              ? DiscoverColumnsV( v, cols )
                              : DiscoverColumnsH( rows, cols );

        // Ensures that at least a non empty column exists and that at least one column
        // is free to grow (MaxW == 0, the last column is used as a fallback).
        if( !hasColumns || !EnsureNotEmptyAndGrowable( cols ) )
        {
            return rows;
        }
        int minWidth = 0;
        int nominalWidth = 0;
        int width = 0;
        foreach( var c in cols )
        {
            c.SettleWidths( out var m, out var n, out var w );
            minWidth += m;
            nominalWidth += n;
            width += w;
        }
        AddSingleCellRowWidths( rows, ref minWidth, ref nominalWidth, ref width );
        var finalCols = cols.ToArray();
        rows = ApplyHOrV( rows, finalCols, width, boxCells: true );
        return new TableLayout( rows, finalCols, columns, minWidth, nominalWidth, width );
    }

    /// <summary>
    /// A row is split into cells only when it is a <see cref="HorizontalContent"/> (see ApplyHOrV): any other
    /// row is a single cell that takes the whole line. Such a row belongs to no column, so the columns alone
    /// would settle the table's widths and the row would simply be wrapped into whatever they need. This
    /// raises the 3 widths to what the widest single cell row needs.
    /// <para>
    /// The table can then be wider than its columns. ColDef.ToWidth doesn't fill the columns up to such a
    /// width: they keep their own.
    /// </para>
    /// </summary>
    static void AddSingleCellRowWidths( IRenderable rows, ref int minWidth, ref int nominalWidth, ref int width )
    {
        if( rows is VerticalContent v )
        {
            foreach( var cell in v.Cells ) OneRow( cell, ref minWidth, ref nominalWidth, ref width );
        }
        else
        {
            OneRow( rows, ref minWidth, ref nominalWidth, ref width );
        }

        static void OneRow( IRenderable row, ref int minWidth, ref int nominalWidth, ref int width )
        {
            // ApplyHOrV lays a Collapsable's content out at (table width - 2): the marker's 2 columns
            // are not available to the row itself, so it needs them on top of its own width.
            if( row is Collapsable c )
            {
                if( c.Content is VerticalContent v )
                {
                    foreach( var cell in v.Cells ) OneCell( cell, 2, ref minWidth, ref nominalWidth, ref width );
                }
                else
                {
                    OneCell( c.Content, 2, ref minWidth, ref nominalWidth, ref width );
                }
            }
            else
            {
                OneCell( row, 0, ref minWidth, ref nominalWidth, ref width );
            }

            static void OneCell( IRenderable cell, int marker, ref int minWidth, ref int nominalWidth, ref int width )
            {
                // A HorizontalContent is split into cells: it is the columns' business.
                if( cell is HorizontalContent h && h.Cells.Length > 0 ) return;
                if( minWidth < cell.MinWidth + marker ) minWidth = cell.MinWidth + marker;
                if( nominalWidth < cell.NominalWidth + marker ) nominalWidth = cell.NominalWidth + marker;
                if( width < cell.Width + marker ) width = cell.Width + marker;
            }
        }
    }

    static bool DiscoverColumnsV( VerticalContent v, List<ColDef> columns )
    {
        bool hasColumns = false;
        foreach( var cell in v.Cells )
        {
            hasColumns |= DiscoverColumnsH( cell, columns );
        }
        return hasColumns;
    }

    static bool DiscoverColumnsH( IRenderable row, List<ColDef> columns )
    {
        if( row is Collapsable c )
        {
            bool hasColumns = false;
            if( c.Content is VerticalContent v )
            {
                foreach( var cell in v.Cells )
                {
                    hasColumns |= Discover( cell, columns, true );
                }
            }
            else
            {
                hasColumns = Discover( c.Content, columns, true );
            }
            return hasColumns;
        }
        return Discover( row, columns, false );

        static bool Discover( IRenderable row, List<ColDef> columns, bool inCollapsable )
        {
            if( row is HorizontalContent h && h.Cells.Length > 0 )
            {
                int common = Math.Min( columns.Count, h.Cells.Length );
                int i = 0;
                for( ; i < common; ++i )
                {
                    columns[i].Add( h.Cells[i], i == 0 && inCollapsable );
                }
                for( ; i < h.Cells.Length; i++ )
                {
                    columns.Add( new ColDef( h.Cells[i], i == 0 && inCollapsable ) );
                }
                return true;
            }
            return false;
        }
    }

    static bool EnsureNotEmptyAndGrowable( List<ColDef> cols )
    {
        bool foundNonEmpty = false;
        bool foundGrowable = false;
        for( int i = 0; i < cols.Count; i++ )
        {
            var c = cols[i];
            foundNonEmpty |= !c.IsEmpty;
            foundGrowable |= c.MaxW == 0;
        }
        if( !foundNonEmpty ) return false;
        if( !foundGrowable ) cols[^1].ClearMaxWidth();
        return true;
    }

    static IRenderable ApplyHOrV( IRenderable rows, ColDef[] columns, int width, bool boxCells )
    {
        if( rows is VerticalContent v )
        {
            return v.ApplyTransform( r => ApplyH( r, columns, width, boxCells ) );
        }
        return ApplyH( rows, columns, width, boxCells );

        static IRenderable ApplyH( IRenderable row, ColDef[] cols, int width, bool boxCells )
        {
            if( row is Collapsable c )
            {
                cols[0].HideCollapsableWidth();
                try
                {
                    return c.WithContent( c.Content is VerticalContent v
                                            ? v.ApplyTransform( r => Apply( r, cols, width - 2, boxCells ) )
                                            : Apply( c.Content, cols, width - 2, boxCells ) );
                }
                finally
                {
                    cols[0].RestoreCollapsableWidth();
                }
            }
            return Apply( row, cols, width, boxCells );

            static IRenderable Apply( IRenderable row, ColDef[] columns, int width, bool boxCells )
            {
                if( row is HorizontalContent h && h.Cells.Length > 0 )
                {
                    int iSet = 0;
                    return boxCells
                        ? h.ApplyTransform( r => r.Box().SetWidth( columns[iSet++].Width ) )
                        : h.ApplyTransform( r => r.SetWidth( columns[iSet++].Width ) );
                }
                return boxCells ? row.Box().SetWidth( width ) : row.SetWidth( width );
            }
        }
    }

    /// <inheritdoc />
    public int Width => _width;

    /// <inheritdoc />
    public int MinWidth => _minWidth;

    /// <inheritdoc />
    public int NominalWidth => _nominalWidth;

    /// <inheritdoc />
    public int Height => _rows.Height;

    /// <inheritdoc />
    public ScreenType ScreenType => _rows.ScreenType;

    /// <summary>
    /// The content rows.
    /// </summary>
    public IRenderable Rows => _rows;

    /// <summary>
    /// The column definition. May be empty.
    /// </summary>
    public ImmutableArray<ColumnDefinition> Columns => _columns;

    /// <inheritdoc />
    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

    /// <inheritdoc />
    public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight ) => _rows.BuildSegmentTree( line, parent, actualHeight );

    /// <inheritdoc />
    public IRenderable SetWidth( int width, bool allowWider )
    {
        if( width < _minWidth ) width = _minWidth;
        if( width == _width ) return this;

        ColDef[] cols;
        int delta = width - _minWidth;
        if( delta == 0 )
        {
            cols = ColDef.ToMinWidth( _cols );
        }
        delta = width - _nominalWidth;
        if( delta == 0 || (delta > 0 && !allowWider ) )
        {
            cols = ColDef.ToNominalWidth( _cols );
        }
        else
        {
            cols = ColDef.ToWidth( _cols, _nominalWidth, width );
        }
        // "width", not "_minWidth": this is what ApplyHOrV gives to a row that spans all the columns
        // (a row that is not a HorizontalContent), and such a row spans the table as it is now, not as
        // narrow as the table could ever be. Create passes its settled width here.
        var rows = ApplyHOrV( _rows, cols, width, boxCells: false );
        return new TableLayout( rows, cols, _columns, _minWidth, _nominalWidth, width );
    }
}
