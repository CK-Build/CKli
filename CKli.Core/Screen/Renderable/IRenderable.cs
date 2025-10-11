using CK.Core;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CKli.Core;

public interface IRenderable
{
    int Height { get; }

    int Width { get; }

    // Temporary.
    int RenderLine<TArg>( int i, TArg arg, ReadOnlySpanAction<char, TArg> render );

    public static readonly IRenderable None = new RenderableUnit();

    private sealed class RenderableUnit : IRenderable
    {
        public int Height => 0;

        public int Width => 0;

        public int RenderLine<TArg>( int i, TArg arg, ReadOnlySpanAction<char, TArg> render ) => 0;
    }
}

public static class RenderableExtensions
{
    public static StringBuilder Render( this IRenderable r, StringBuilder b )
    {
        for( int i = 0; i < r.Height; i++ )
        {
            r.RenderLine( i, b, ( span, b ) => b.Append( span ) );
            b.AppendLine();
        }
        return b;
    }

    public static string RenderAsString( this IRenderable r ) => r.Render( new StringBuilder() ).ToString();

    public static IRenderable Box( this IRenderable r, int top = 0, int left = 0, int bottom = 0, int right = 0 )
    {
        if( r is ContentBox b ) return b.WithPadding( top, left, bottom, right );
        return new ContentBox( r, new Padding( (byte)top, (byte)left, (byte)bottom, (byte)right ) );
    }

    public static IRenderable AddLeft( this IRenderable r, bool condition, params ReadOnlySpan<IRenderable?> horizontalContent )
    {
        if( !condition || horizontalContent.Length == 0 ) return r;
        if( r is HorizontalContent h ) return h.Prepend( horizontalContent );
        int flattenedLength = VerticalContent.ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return r;
        var skipThis = r.Width == 0;
        if( skipThis ) --flattenedLength;
        var a = new IRenderable[flattenedLength + 1];
        a[^1] = r;
        return HorizontalContent.FillNewContent( horizontalContent, hasSpecial, a, 0 );
    }
    public static IRenderable AddLeft( this IRenderable r, params ReadOnlySpan<IRenderable?> horizontalContent ) => AddLeft( r, true, horizontalContent );

    public static IRenderable AddLeft( this IRenderable r, params IEnumerable<IRenderable?> horizontalContent ) => AddLeft( r, [.. horizontalContent] );

    public static IRenderable AddLeft( this IRenderable r, params IEnumerable<object?> horizontalContent ) => AddLeft( r, true, Flatten( horizontalContent ) );

    public static IRenderable AddLeft( this IRenderable r, bool condition, params IEnumerable<IRenderable?> horizontalContent ) => AddLeft( r, condition, [.. horizontalContent] );

    public static IRenderable AddLeft( this IRenderable r, bool condition, params IEnumerable<object?> horizontalContent ) => AddLeft( r, condition, Flatten( horizontalContent ) );

    public static IRenderable AddRight( this IRenderable r, bool condition, params ReadOnlySpan<IRenderable?> horizontalContent )
    {
        if( !condition || horizontalContent.Length == 0 ) return r;
        if( r is HorizontalContent h ) return h.Append( horizontalContent );
        int flattenedLength = VerticalContent.ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return r;
        var skipThis = r.Width == 0;
        if( skipThis ) --flattenedLength;
        var a = new IRenderable[1 + flattenedLength];
        a[0] = r;
        return HorizontalContent.FillNewContent( horizontalContent, hasSpecial, a, skipThis ? 0 : 1 );
    }

    public static IRenderable AddRight( this IRenderable r, params ReadOnlySpan<IRenderable?> horizontalContent ) => AddRight( r, true, horizontalContent );

    public static IRenderable AddRight( this IRenderable r, params IEnumerable<IRenderable?> horizontalContent ) => AddRight( r, [.. horizontalContent] );

    public static IRenderable AddRight( this IRenderable r, params IEnumerable<object?> horizontalContent ) => AddRight( r, true, Flatten( horizontalContent ) );

    public static IRenderable AddRight( this IRenderable r, bool condition, params IEnumerable<IRenderable?> horizontalContent ) => AddRight( r, condition, [.. horizontalContent] );

    public static IRenderable AddRight( this IRenderable r, bool condition, params IEnumerable<object?> horizontalContent ) => AddRight( r, condition, Flatten( horizontalContent ) );

    public static IRenderable AddBelow( this IRenderable r, bool condition, params ReadOnlySpan<IRenderable?> verticalContent )
    {
        if( !condition || verticalContent.Length == 0 ) return r;
        if( r is VerticalContent v ) return v.Append( verticalContent );
        int flattenedLength = VerticalContent.ComputeActualContentLength( verticalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return r;
        var skipThis = r.Height == 0;
        if( skipThis ) --flattenedLength;
        var a = new IRenderable[1 + flattenedLength];
        a[0] = r;
        return VerticalContent.FillNewContent( verticalContent, hasSpecial, a, skipThis ? 0 : 1 );
    }

    public static IRenderable AddBelow( this IRenderable r, params ReadOnlySpan<IRenderable?> verticalContent ) => AddBelow( r, true, verticalContent );

    public static IRenderable AddBelow( this IRenderable r, params IEnumerable<IRenderable?> verticalContent ) => AddBelow( r, [.. verticalContent] );

    public static IRenderable AddBelow( this IRenderable r, params IEnumerable<object?> verticalContent ) => AddBelow( r, true, Flatten( verticalContent ) );

    public static IRenderable AddBelow( this IRenderable r, bool condition, params IEnumerable<IRenderable?> verticalContent ) => AddBelow( r, condition, [.. verticalContent] );

    public static IRenderable AddBelow( this IRenderable r, bool condition, params IEnumerable<object?> verticalContent ) => AddBelow( r, condition, Flatten( verticalContent ) );

    static IEnumerable<IRenderable> Flatten( IEnumerable<object?> objects )
    {
        foreach( var o in objects )
        {
            if( o is IRenderable r ) yield return r;
            else if( o is IEnumerable<IRenderable?> m )
            {
                foreach( var r2 in m )
                {
                    if( r2 != null ) yield return r2;
                }
            }
            else if( o is IEnumerable<object?> mO )
            {
                foreach( var r3 in Flatten( mO ) ) yield return r3;
            }
            else if( o != null )
            {
                Throw.ArgumentException( $"Invalid object type '{o.GetType()}' in enumeration: must be ILineRenderable, IEnumerable<ILineRenderable>, IEnumerable<object> (or null)." );
            }
        }
    }
}
