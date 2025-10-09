using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Core;

public sealed class HorizontalContent : ILineRenderable
{
    readonly ImmutableArray<ILineRenderable> _cells;
    readonly int _width;
    readonly int _height;

    public HorizontalContent( params ImmutableArray<ILineRenderable> cells )
    {
        _cells = cells;
        _width = ComputeWith( cells );
        _height = ComputeHeight( cells );
    }

    static int ComputeWith( ImmutableArray<ILineRenderable> cells )
    {
        int w = 0;
        foreach( var cell in cells )
        {
            w += cell.Width;
        }
        return w;
    }

    static int ComputeHeight( ImmutableArray<ILineRenderable> cells )
    {
        int h = 0;
        foreach( var cell in cells )
        {
            if( h < cell.Height ) h = cell.Height;
        }
        return h;
    }

    public int Height => _height;

    public int Width => _width;

    public int RenderLine<TArg>( int i, TArg arg, ReadOnlySpanAction<char, TArg> render )
    {
        int w = 0;
        if( i >= 0 && i < _height )
        {
            foreach( var cell in _cells )
            {
                w += cell.RenderLine( i, arg, render );
            }
        }
        return w;
    }

    public HorizontalContent Append( ReadOnlySpan<ILineRenderable?> horizontalContent )
    {
        if( horizontalContent.Length == 0 ) return this;
        int flattenedLength = ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return this;

        var a = new ILineRenderable[ _cells.Length + flattenedLength];
        _cells.CopyTo( a, 0 );
        return FillNewContent( horizontalContent, hasSpecial, a, _cells.Length );
    }

    public HorizontalContent Prepend( ReadOnlySpan<ILineRenderable?> horizontalContent )
    {
        if( horizontalContent.Length == 0 ) return this;
        int flattenedLength = ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return this;
        var a = new ILineRenderable[ flattenedLength + _cells.Length ];
        _cells.CopyTo( a, _cells.Length );
        return FillNewContent( horizontalContent, hasSpecial, a, 0 );
    }

    internal static int ComputeActualContentLength( ReadOnlySpan<ILineRenderable?> horizontalContent, out bool hasSpecial )
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

    internal static HorizontalContent FillNewContent( ReadOnlySpan<ILineRenderable?> horizontalContent, bool hasSpecial, ILineRenderable[] newContent, int idxCopy )
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
        return new HorizontalContent( ImmutableCollectionsMarshal.AsImmutableArray( newContent ) );
    }


}
