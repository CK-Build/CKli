using CK.Core;
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Core;

public sealed class VerticalContent : IRenderable
{
    readonly ImmutableArray<IRenderable> _cells;
    readonly ScreenType _screenType;
    readonly int _width;
    readonly int _height;
    readonly int _minWidth;
    readonly int _nominalWidth;

    public VerticalContent( ScreenType screenType, params ImmutableArray<IRenderable> cells )
    {
        _screenType = screenType;
        _cells = cells;
        _height = ComputeHeight( cells );
        _width = _height > 0 ? ComputeWidth( cells, out _minWidth, out _nominalWidth ) : 0;
    }

    static int ComputeWidth( ImmutableArray<IRenderable> cells, out int minWidth, out int nominalWidth )
    {
        minWidth = 0;
        nominalWidth = 0;
        int w = 0;
        foreach( var cell in cells )
        {
            if( w < cell.Width ) w = cell.Width;
            if( minWidth < cell.MinWidth ) minWidth = cell.MinWidth;
            if( nominalWidth < cell.NominalWidth ) nominalWidth = cell.NominalWidth;
        }
        return w;
    }

    static int ComputeHeight( ImmutableArray<IRenderable> cells )
    {
        int h = 0;
        foreach( var cell in cells )
        {
            h += cell.Height;
        }
        return h;
    }

    public ScreenType ScreenType => _screenType;

    public int Height => _height;

    public int Width => _width;

    public int MinWidth => _minWidth;

    public int NominalWidth => _nominalWidth;

    public ImmutableArray<IRenderable> Cells => _cells;

    public IRenderable SetWidth( int width, bool allowWider )
    {
        if( width < _minWidth ) width = _minWidth;
        if( width == _width ) return this;
        return ApplyTransform( r => r.SetWidth( width, allowWider ) );
    }

    /// <summary>
    /// Applies a transform function to all <see cref="Cells"/>.
    /// </summary>
    /// <param name="f">The transformation to apply.</param>
    /// <returns>A new vertical content, <see cref="ScreenType.RenderableUnit"/> or this if nothing changed.</returns>
    public IRenderable ApplyTransform( Func<IRenderable,IRenderable?> f )
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
                if( newC != null && newC.Height > 0 ) b.Add( newC );
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
                    : new VerticalContent( _screenType, b.DrainToImmutable() );
    }

    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

    public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight )
    {
        Throw.CheckArgument( line >= 0 && line < actualHeight && actualHeight >= Height );
        Throw.DebugAssert( line >= 0 );
        // There is currently no "VAlign". It could be bottom/top/middle/distribute.
        // We ignore the actualHeight.
        if( line < _height )
        {
            foreach( var cell in _cells )
            {
                int next = line - cell.Height;
                if( next < 0 )
                {
                    cell.BuildSegmentTree( line, parent, cell.Height );
                    return;
                }
                line = next;
            }
        }
    }

    public VerticalContent Append( ReadOnlySpan<IRenderable?> verticalContent )
    {
        if( verticalContent.Length == 0 ) return this;
        int flattenedLength = ComputeActualContentLength( verticalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return this;
        var newContent = new IRenderable[_cells.Length + flattenedLength];
        _cells.CopyTo( newContent, 0 );
        return FillNewContent( _screenType, verticalContent, hasSpecial, newContent, _cells.Length );
    }

    public VerticalContent Prepend( ReadOnlySpan<IRenderable?> verticalContent )
    {
        if( verticalContent.Length == 0 ) return this;
        int flattenedLength = ComputeActualContentLength( verticalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return this;
        var newContent = new IRenderable[ flattenedLength + _cells.Length ];
        _cells.CopyTo( newContent, flattenedLength );
        return FillNewContent( _screenType, verticalContent, hasSpecial, newContent, 0 );
    }

    internal static int ComputeActualContentLength( ReadOnlySpan<IRenderable?> verticalContent, out bool hasSpecial )
    {
        int flattenedLength = 0;
        hasSpecial = false;
        foreach( var newOne in verticalContent )
        {
            if( newOne is VerticalContent v )
            {
                hasSpecial = true;
                flattenedLength += v._cells.Length;
                continue;
            }
            if( newOne is null || newOne.Height == 0 )
            {
                hasSpecial = true;
                continue;
            }
            flattenedLength++;
        }
        return flattenedLength;
    }

    internal static VerticalContent FillNewContent( ScreenType screenType,
                                                    ReadOnlySpan<IRenderable?> verticalContent,
                                                    bool hasSpecial,
                                                    IRenderable[] newContent,
                                                    int idxCopy )
    {
        if( hasSpecial )
        {
            foreach( var newOne in verticalContent )
            {
                if( newOne is VerticalContent v )
                {
                    v._cells.CopyTo( newContent, idxCopy );
                    idxCopy += v._cells.Length;
                    continue;
                }
                if( newOne is null || newOne.Height == 0 )
                {
                    continue;
                }
                newContent[idxCopy++] = newOne;
            }
        }
        else
        {
            verticalContent.CopyTo( newContent.AsSpan( idxCopy )! );
        }
        return new VerticalContent( screenType, ImmutableCollectionsMarshal.AsImmutableArray( newContent ) );
    }
}
