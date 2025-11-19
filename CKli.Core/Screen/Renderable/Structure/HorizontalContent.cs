using CK.Core;
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Core;

/// <summary>
/// An horizontal set of renderables.
/// </summary>
public sealed class HorizontalContent : IRenderable
{
    readonly ImmutableArray<IRenderable> _cells;
    readonly ScreenType _screenType;
    readonly int _width;
    readonly int _height;
    readonly int _minWidth;
    readonly int _nominalWidth;

    /// <summary>
    /// Initializes a new horizontal content.
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="cells">The content.</param>
    public HorizontalContent( ScreenType screenType, params ImmutableArray<IRenderable> cells )
    {
        _screenType = screenType;
        _cells = cells;
        _width = ComputeWidth( cells, out _minWidth, out _nominalWidth );
        _height = _width > 0 ? ComputeHeight( cells ) : 0;
    }

    static int ComputeWidth( ImmutableArray<IRenderable> cells, out int minWidth, out int nominalWidth )
    {
        nominalWidth = 0;
        minWidth = 0;
        int w = 0;
        foreach( var cell in cells )
        {
            w += cell.Width;
            minWidth += cell.MinWidth;
            nominalWidth += cell.NominalWidth;
        }
        return w;
    }

    static int ComputeHeight( ImmutableArray<IRenderable> cells )
    {
        int h = 0;
        foreach( var cell in cells )
        {
            if( h < cell.Height ) h = cell.Height;
        }
        return h;
    }

    /// <inheritdoc />
    public ScreenType ScreenType => _screenType;

    /// <inheritdoc />
    public int Height => _height;

    /// <inheritdoc />
    public int Width => _width;

    /// <inheritdoc />
    public int MinWidth => _minWidth;

    /// <inheritdoc />
    public int NominalWidth => _nominalWidth;

    /// <summary>
    /// Gets the content.
    /// </summary>
    public ImmutableArray<IRenderable> Cells => _cells;

    /// <inheritdoc />
    public IRenderable SetWidth( int width, bool allowWider )
    {
        if( width < _minWidth ) width = _minWidth;
        if( width == _width ) return this;

        int delta = width - _minWidth;
        if( delta == 0 )
        {
            return ApplyTransform( r => r.SetWidth( 0, false ) );
        }
        delta = width - _nominalWidth;
        if( delta == 0 || (delta > 0 && !allowWider ) )
        {
            return ApplyTransform( r => r.SetWidth( r.NominalWidth, false ) );
        }
        double ratio = (double)width / _nominalWidth;
        var widths = new int[_cells.Length];
        int sum = 0;
        for( int i = 0; i < _cells.Length; i++ )
        {
            var c = _cells[i];
            int w = Math.Max( (int)Math.Round( (ratio * c.NominalWidth) + 0.5, MidpointRounding.ToZero ), c.MinWidth );
            widths[i] = w;
            sum += w;
        }
        delta = width - sum;
        if( delta > 0 )
        {
            do
            {
                for( int i = _cells.Length - 1; i >= 0; i-- )
                {
                    widths[i]++;
                    if( --delta == 0 ) break;
                }
            }
            while( delta != 0 );
        }
        else if( delta < 0 )
        {
            do
            {
                for( int i = _cells.Length - 1; i >= 0; i-- )
                {
                    var c = _cells[i];
                    if( widths[i] > c.MinWidth )
                    {
                        widths[i]--;
                        if( ++delta == 0 ) break;
                    }
                }
            }
            while( delta != 0 );
        }
        int iSet = 0;
        return ApplyTransform( r => r.SetWidth( widths[iSet++], allowWider ) );
    }

    /// <summary>
    /// Applies a transform function to all <see cref="Cells"/>.
    /// </summary>
    /// <param name="f">The transformation to apply.</param>
    /// <returns>A new horizontal content, <see cref="ScreenType.RenderableUnit"/> or this if nothing changed.</returns>
    public IRenderable ApplyTransform( Func<IRenderable, IRenderable?> f )
    {
        ImmutableArray<IRenderable>.Builder? b = null;
        for( int i = 0; i < _cells.Length; i++ )
        {
            var c = _cells[i];
            var newC = f( c );
            if( newC != c )
            {
                if( b == null )
                {
                    b = ImmutableArray.CreateBuilder<IRenderable>( _cells.Length );
                    b.AddRange( _cells, i );
                }
                if( newC != null && newC.Width > 0 ) b.Add( newC );
            }
            else
            {
                b?.Add( c );
            }
        }
        return b == null
                ? this
                : b.Count == 0
                    ? _screenType.Unit
                    : new HorizontalContent( _screenType, b.DrainToImmutable() );
    }


    /// <inheritdoc />
    public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight )
    {
        Throw.CheckArgument( line >= 0 && line < actualHeight );
        if( line < _height )
        {
            foreach( var cell in _cells )
            {
                // This is currently the only place where the actualHeight is relevant:
                // children now know their vertical playground that is the max(Height) of
                // their siblings.
                cell.BuildSegmentTree( line, parent, actualHeight );
            }
        }
    }

    /// <summary>
    /// Returns a new horizontal content with the appended content.
    /// </summary>
    /// <param name="horizontalContent">Content to append.</param>
    /// <returns>This or a new horizontal content.</returns>
    public HorizontalContent Append( ReadOnlySpan<IRenderable?> horizontalContent )
    {
        if( horizontalContent.Length == 0 ) return this;
        int flattenedLength = ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return this;

        var a = new IRenderable[ _cells.Length + flattenedLength];
        _cells.CopyTo( a, 0 );
        return FillNewContent( _screenType, horizontalContent, hasSpecial, a, _cells.Length );
    }

    /// <summary>
    /// Returns a new horizontal content with some content before this one.
    /// </summary>
    /// <param name="horizontalContent">Content to insert before this one.</param>
    /// <returns>This or a new horizontal content.</returns>
    public HorizontalContent Prepend( ReadOnlySpan<IRenderable?> horizontalContent )
    {
        if( horizontalContent.Length == 0 ) return this;
        int flattenedLength = ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return this;
        var a = new IRenderable[ flattenedLength + _cells.Length ];
        _cells.CopyTo( a, flattenedLength );
        return FillNewContent( _screenType, horizontalContent, hasSpecial, a, 0 );
    }

    /// <inheritdoc />
    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

    internal static int ComputeActualContentLength( ReadOnlySpan<IRenderable?> horizontalContent, out bool hasSpecial )
    {
        int flattenedLength = 0;
        hasSpecial = false;
        foreach( var newOne in horizontalContent )
        {
            if( newOne is HorizontalContent h )
            {
                hasSpecial = true;
                flattenedLength += h._cells.Length;
                continue;
            }
            if( newOne is null || newOne.Width == 0 )
            {
                hasSpecial = true;
                continue;
            }
            flattenedLength++;
        }
        return flattenedLength;
    }

    internal static HorizontalContent FillNewContent( ScreenType screenType,
                                                      ReadOnlySpan<IRenderable?> horizontalContent,
                                                      bool hasSpecial,
                                                      IRenderable[] newContent,
                                                      int idxCopy )
    {
        if( hasSpecial )
        {
            foreach( var newOne in horizontalContent )
            {
                if( newOne is HorizontalContent h )
                {
                    h._cells.CopyTo( newContent, idxCopy );
                    idxCopy += h._cells.Length;
                    continue;
                }
                if( newOne is null || newOne.Width == 0 )
                {
                    continue;
                }
                newContent[idxCopy++] = newOne;
            }
        }
        else
        {
            horizontalContent.CopyTo( newContent.AsSpan(idxCopy)! );
        }
        return new HorizontalContent( screenType, ImmutableCollectionsMarshal.AsImmutableArray( newContent ) );
    }

}
