using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Core;

public sealed class VerticalContent : IRenderable
{
    readonly ImmutableArray<IRenderable> _cells;
    readonly int _width;
    readonly int _height;

    public VerticalContent( params ImmutableArray<IRenderable> cells )
    {
        _cells = cells;
        _width = ComputeWith( cells );
        _height = ComputeHeight( cells );
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

    public int Height => _height;

    public int Width => _width;

    public int RenderLine<TArg>( int i, TArg arg, ReadOnlySpanAction<char, TArg> render )
    {
        if( i >= 0 && i < _height )
        {
            foreach( var cell in _cells )
            {
                int next = i - cell.Height;
                if( next < 0 )
                {
                    return cell.RenderLine( i, arg, render );
                }
                i = next;
            }
        }
        return 0;
    }

    public VerticalContent Append( ReadOnlySpan<IRenderable?> verticalContent )
    {
        if( verticalContent.Length == 0 ) return this;
        int flattenedLength = ComputeActualContentLength( verticalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return this;
        var newContent = new IRenderable[_cells.Length + flattenedLength];
        _cells.CopyTo( newContent, 0 );
        return FillNewContent( verticalContent, hasSpecial, newContent, _cells.Length );
    }

    public VerticalContent Prepend( ReadOnlySpan<IRenderable?> verticalContent )
    {
        if( verticalContent.Length == 0 ) return this;
        int flattenedLength = ComputeActualContentLength( verticalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return this;
        var newContent = new IRenderable[ flattenedLength + _cells.Length ];
        _cells.CopyTo( newContent, _cells.Length );
        return FillNewContent( verticalContent, hasSpecial, newContent, 0 );
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

    internal static VerticalContent FillNewContent( ReadOnlySpan<IRenderable?> verticalContent, bool hasSpecial, IRenderable[] newContent, int idxCopy )
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
        return new VerticalContent( ImmutableCollectionsMarshal.AsImmutableArray( newContent ) );
    }

}
