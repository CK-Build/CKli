using CK.Core;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Core;

public sealed class HorizontalContent : IRenderable
{
    readonly ImmutableArray<IRenderable> _cells;
    readonly ScreenType _screenType;
    readonly int _width;
    readonly int _height;
    readonly int _minWidth;

    public HorizontalContent( ScreenType screenType, params ImmutableArray<IRenderable> cells )
    {
        _screenType = screenType;
        _cells = cells;
        _width = ComputeWidth( cells, out _minWidth );
        _height = _width > 0 ? ComputeHeight( cells ) : 0;
    }

    static int ComputeWidth( ImmutableArray<IRenderable> cells, out int minWidth )
    {
        minWidth = 0;
        int w = 0;
        foreach( var cell in cells )
        {
            w += cell.Width;
            minWidth += cell.MinWidth;
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

    public ScreenType ScreenType => _screenType;

    public int Height => _height;

    public int Width => _width;

    public int MinWidth => _minWidth;

    public ImmutableArray<IRenderable> Cells => _cells;

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

    public HorizontalContent Append( ReadOnlySpan<IRenderable?> horizontalContent )
    {
        if( horizontalContent.Length == 0 ) return this;
        int flattenedLength = ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return this;

        var a = new IRenderable[ _cells.Length + flattenedLength];
        _cells.CopyTo( a, 0 );
        return FillNewContent( _screenType, horizontalContent, hasSpecial, a, _cells.Length );
    }

    public HorizontalContent Prepend( ReadOnlySpan<IRenderable?> horizontalContent )
    {
        if( horizontalContent.Length == 0 ) return this;
        int flattenedLength = ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return this;
        var a = new IRenderable[ flattenedLength + _cells.Length ];
        _cells.CopyTo( a, flattenedLength );
        return FillNewContent( _screenType, horizontalContent, hasSpecial, a, 0 );
    }

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
