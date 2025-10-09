using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace CKli.Core;

public interface ILineRenderable
{
    int Height { get; }

    int Width { get; }

    int RenderLine<TArg>( int i, TArg arg, ReadOnlySpanAction<char, TArg> render );

    public static readonly ILineRenderable None = new NoneRenderable();

    public static readonly ILineRenderable EmptyString = new EmptyStringRenderable();

    sealed class NoneRenderable : ILineRenderable
    {
        public int Height => 0;

        public int Width => 0;

        public int RenderLine<TArg>( int i, TArg arg, ReadOnlySpanAction<char, TArg> render ) => 0;
    }

    sealed class EmptyStringRenderable : ILineRenderable
    {
        public int Height => 1;

        public int Width => 0;

        public int RenderLine<TArg>( int i, TArg arg, ReadOnlySpanAction<char, TArg> render ) => 0;
    }
}

public static class LineRenderableExtensions
{
    public static ILineRenderable Box( this ILineRenderable r, int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        if( r is ContentBox b ) return b.WithPadding( top, left, bottom, right );
        return new ContentBox( r, new Padding( (byte)top, (byte)left, (byte)bottom, (byte)right ) );
    }

    public static ILineRenderable AddLeft( this ILineRenderable r, bool condition, params ReadOnlySpan<ILineRenderable?> horizontalContent )
    {
        if( !condition || horizontalContent.Length == 0 ) return r;
        if( r is HorizontalContent h ) return h.Prepend( horizontalContent );
        int flattenedLength = VerticalContent.ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return r;
        var skipThis = r.Width == 0;
        if( skipThis ) --flattenedLength;
        var a = new ILineRenderable[1 + flattenedLength];
        a[0] = r;
        return HorizontalContent.FillNewContent( horizontalContent, hasSpecial, a, skipThis ? 0 : 1 );
    }
    public static ILineRenderable AddLeft( this ILineRenderable r, params ReadOnlySpan<ILineRenderable?> horizontalContent ) => AddLeft( r, true, horizontalContent );

    public static ILineRenderable AddLeft( this ILineRenderable r, params IEnumerable<ILineRenderable?> horizontalContent ) => AddLeft( r, [.. horizontalContent] );

    public static ILineRenderable AddLeft( this ILineRenderable r, bool condition, params IEnumerable<ILineRenderable?> horizontalContent ) => AddLeft( r, condition, [.. horizontalContent] );

    public static ILineRenderable AddRight( this ILineRenderable r, bool condition, params ReadOnlySpan<ILineRenderable?> horizontalContent )
    {
        if( !condition || horizontalContent.Length == 0 ) return r;
        if( r is HorizontalContent h ) return h.Append( horizontalContent );
        int flattenedLength = VerticalContent.ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return r;
        var skipThis = r.Width == 0;
        if( skipThis ) --flattenedLength;
        var a = new ILineRenderable[1 + flattenedLength];
        a[0] = r;
        return HorizontalContent.FillNewContent( horizontalContent, hasSpecial, a, skipThis ? 0 : 1 );
    }
    public static ILineRenderable AddRight( this ILineRenderable r, params ReadOnlySpan<ILineRenderable?> horizontalContent ) => AddLeft( r, true, horizontalContent );

    public static ILineRenderable AddRight( this ILineRenderable r, params IEnumerable<ILineRenderable?> horizontalContent ) => AddLeft( r, [.. horizontalContent] );

    public static ILineRenderable AddRight( this ILineRenderable r, bool condition, params IEnumerable<ILineRenderable?> horizontalContent ) => AddLeft( r, condition, [.. horizontalContent] );

    public static ILineRenderable AddBelow( this ILineRenderable r, bool condition, params ReadOnlySpan<ILineRenderable?> verticalContent )
    {
        if( !condition || verticalContent.Length == 0 ) return r;
        if( r is VerticalContent v ) return v.Append( verticalContent );
        int flattenedLength = VerticalContent.ComputeActualContentLength( verticalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return r;
        var skipThis = r.Height == 0;
        if( skipThis ) --flattenedLength;
        var a = new ILineRenderable[1 + flattenedLength];
        a[0] = r;
        return VerticalContent.FillNewContent( verticalContent, hasSpecial, a, skipThis ? 0 : 1 );
    }

    public static ILineRenderable AddBelow( this ILineRenderable r, params ReadOnlySpan<ILineRenderable?> verticalContent ) => AddBelow( r, true, verticalContent );

    public static ILineRenderable AddBelow( this ILineRenderable r, params IEnumerable<ILineRenderable?> verticalContent )
    {
        return AddBelow( r, [.. verticalContent] );
    }

    public static ILineRenderable AddBelow( this ILineRenderable r, bool condition, params IEnumerable<ILineRenderable?> verticalContent ) => AddBelow( r, condition, [.. verticalContent] );
}
