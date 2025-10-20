using CK.Core;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Core;

public sealed class VerticalContent : IRenderable
{
    readonly ImmutableArray<IRenderable> _cells;
    readonly ScreenType _screenType;
    readonly int _width;
    readonly int _height;

    public VerticalContent( ScreenType screenType, params ImmutableArray<IRenderable> cells )
    {
        _screenType = screenType;
        _cells = cells;
        _height = ComputeHeight( cells );
        _width = _height > 0 ? ComputeWith( cells ) : 0;
    }

    static int ComputeWith( ImmutableArray<IRenderable> cells )
    {
        int w = 0;
        foreach( var cell in cells )
        {
            if( w < cell.Width ) w = cell.Width;
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

    public ImmutableArray<IRenderable> Cells => _cells;

    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

    public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight )
    {
        Throw.CheckArgument( line >= 0 && line < actualHeight && actualHeight >= Height );
        Throw.DebugAssert( line >= 0 );
        // There is currently no "VJustify". It could be bottom/top/center/distribute.
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
