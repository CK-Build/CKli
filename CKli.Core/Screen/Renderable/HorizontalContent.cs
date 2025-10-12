using CK.Core;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Core;

public sealed class HorizontalContent : IRenderable
{
    readonly ImmutableArray<IRenderable> _cells;
    readonly int _width;
    readonly int _height;

    public HorizontalContent( params ImmutableArray<IRenderable> cells )
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
            w += cell.Width;
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

    public int Height => _height;

    public int Width => _width;

    public SegmentRenderer CollectRenderer( int line, SegmentRenderer previous )
    {
        Throw.DebugAssert( line >= 0 );
        if( line < _height )
        {
            return new Renderer( previous, _width, previous.CreateRendererGroup( Width, _cells, line ) );
        }
        return previous;
    }

    sealed class Renderer : SegmentRenderer
    {
        readonly SegmentRenderer _head;

        public Renderer( SegmentRenderer parent, int length, SegmentRenderer head )
            : base( parent, length )
        {
            _head = head;
        }

        protected override void Render( IRenderTarget target )
        {
            var s = _head.Next;
            while( s != null )
            {
                s.Render();
                s = s.Next;
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
        return FillNewContent( horizontalContent, hasSpecial, a, _cells.Length );
    }

    public HorizontalContent Prepend( ReadOnlySpan<IRenderable?> horizontalContent )
    {
        if( horizontalContent.Length == 0 ) return this;
        int flattenedLength = ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return this;
        var a = new IRenderable[ flattenedLength + _cells.Length ];
        _cells.CopyTo( a, _cells.Length );
        return FillNewContent( horizontalContent, hasSpecial, a, 0 );
    }

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

    internal static HorizontalContent FillNewContent( ReadOnlySpan<IRenderable?> horizontalContent, bool hasSpecial, IRenderable[] newContent, int idxCopy )
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
