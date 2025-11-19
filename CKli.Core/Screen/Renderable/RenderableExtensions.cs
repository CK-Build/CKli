using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace CKli.Core;

/// <summary>
/// Extends <see cref="IRenderable"/>.
/// </summary>
public static class RenderableExtensions
{
    /// <summary>
    /// Renders this into the provided <see cref="IRenderTarget"/>.
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <param name="target">The target renderer.</param>
    /// <param name="newLine">False to not append a new line after the renderable.</param>
    public static void Render( this IRenderable r, IRenderTarget target, bool newLine = true ) => SegmentRenderer.Render( r, target, newLine );

    /// <summary>
    /// Renders these renderables into the provided <see cref="IRenderTarget"/>.
    /// </summary>
    /// <param name="renderables">This renderables.</param>
    /// <param name="target">The target renderer.</param>
    /// <param name="newLine">False to not append a new line after the last renderable.</param>
    public static void Render( this IEnumerable<IRenderable> renderables, IRenderTarget target, bool newLine = true ) => SegmentRenderer.Render( renderables, target, newLine );

    /// <summary>
    /// Renders this into the provided string builder.
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <param name="b">The target builder.</param>
    /// <param name="newLine">False to not append a new line after the renderable.</param>
    /// <returns>The builder.</returns>
    public static StringBuilder RenderAsString( this IRenderable r, StringBuilder b, bool newLine = true )
    {
        SegmentRenderer.Render( r, new StringScreen.RenderTarget( b ), newLine );
        return b;
    }

    /// <summary>
    /// Renders this as a string.
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <returns>The rendered string.</returns>
    public static string RenderAsString( this IRenderable r ) => r.RenderAsString( new StringBuilder() ).ToString();

    /// <summary>
    /// Wraps this renderable into an hyper link.
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <param name="target">The target url.</param>
    /// <returns>An hyperlink around this renderable.</returns>
    public static HyperLink HyperLink( this IRenderable r, Uri target )
    {
        return r is HyperLink h
                ? h.WithTarget( target )
                : new HyperLink( r, target );
    }

    /// <summary>
    /// Wraps this renderable into a collapsable.
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <returns>A Collapsable around this renderable.</returns>
    [return: NotNullIfNotNull( nameof( r ) )]
    public static Collapsable? Collapsable( this IRenderable? r )
    {
        return r is null
                ? null
                : r is Collapsable c
                    ? c.WithContent( r )
                    : new Collapsable( r );
    }

    /// <summary>
    /// Tries to apply a table layout to this renderable.
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <param name="columns">The optional columns definition.</param>
    /// <returns>A table layout is possible.</returns>
    public static IRenderable TableLayout( this IRenderable r, params ImmutableArray<ColumnDefinition> columns )
    {
        return r is TableLayout t
                ? Core.TableLayout.Create( t.Rows, columns )
                : Core.TableLayout.Create( r, columns );
    }

    /// <summary>
    /// Applies padding and margin to this renderable by creating a new <see cref="ContentBox"/>
    /// around this renderable.
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <param name="paddingTop">Optional top padding.</param>
    /// <param name="paddingLeft">Optional left padding.</param>
    /// <param name="paddingBottom">Optional bottom padding.</param>
    /// <param name="paddingRight">Optional right padding.</param>
    /// <param name="marginTop">Optional top margin.</param>
    /// <param name="marginLeft">Optional left margin.</param>
    /// <param name="marginBottom">Optional bottom margin.</param>
    /// <param name="marginRight">Optional right margin.</param>
    /// <returns>A content box.</returns>
    public static ContentBox Box( this IRenderable r,
                                  int paddingTop = 0, int paddingLeft = 0, int paddingBottom = 0, int paddingRight = 0,
                                  int marginTop = 0, int marginLeft = 0, int marginBottom = 0, int marginRight = 0 )
    {
        if( r is ContentBox b ) return b.AddPadding( paddingTop, paddingLeft, paddingBottom, paddingRight )
                                        .AddMargin( marginTop, marginLeft, marginBottom, marginRight );
        return new ContentBox( r,
                               paddingTop, paddingLeft, paddingBottom, paddingRight,
                               marginTop, marginLeft, marginBottom, marginRight );
    }

    /// <summary>
    /// Applies <see cref="TextStyle"/>, padding, margin and/or <see cref="ContentAlign"/> to this renderable by
    /// creating a new <see cref="ContentBox"/> around this renderable.
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <param name="style">Style to apply.</param>
    /// <param name="paddingTop">Optional top padding.</param>
    /// <param name="paddingLeft">Optional left padding.</param>
    /// <param name="paddingBottom">Optional bottom padding.</param>
    /// <param name="paddingRight">Optional right padding.</param>
    /// <param name="marginTop">Optional top margin.</param>
    /// <param name="marginLeft">Optional left margin.</param>
    /// <param name="marginBottom">Optional bottom margin.</param>
    /// <param name="marginRight">Optional right margin.</param>
    /// <param name="align">Optional alignment to apply.</param>
    /// <returns>A content box.</returns>
    public static ContentBox Box( this IRenderable r,
                                  TextStyle style,
                                  int paddingTop = 0, int paddingLeft = 0, int paddingBottom = 0, int paddingRight = 0,
                                  int marginTop = 0, int marginLeft = 0, int marginBottom = 0, int marginRight = 0,
                                  ContentAlign align = default )
    {
        if( r is ContentBox b ) return b.AddPadding( paddingTop, paddingLeft, paddingBottom, paddingRight )
                                        .AddMargin( marginTop, marginLeft, marginBottom, marginRight )
                                        .WithStyle( style )
                                        .WithAlign( align );
        return new ContentBox( r, paddingTop, paddingLeft, paddingBottom, paddingRight,
                                  marginTop, marginLeft, marginBottom, marginRight,
                                  style: style,
                                  align: align );
    }

    /// <summary>
    /// Applies <see cref="ContentAlign"/>, padding and/or margin to this renderable by
    /// creating a new <see cref="ContentBox"/> around this renderable.
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <param name="align">Alignment to apply.</param>
    /// <param name="paddingTop">Optional top padding.</param>
    /// <param name="paddingLeft">Optional left padding.</param>
    /// <param name="paddingBottom">Optional bottom padding.</param>
    /// <param name="paddingRight">Optional right padding.</param>
    /// <param name="marginTop">Optional top margin.</param>
    /// <param name="marginLeft">Optional left margin.</param>
    /// <param name="marginBottom">Optional bottom margin.</param>
    /// <param name="marginRight">Optional right margin.</param>
    /// <returns>A content box.</returns>
    public static ContentBox Box( this IRenderable r,
                                  ContentAlign align,
                                  int paddingTop = 0, int paddingLeft = 0, int paddingBottom = 0, int paddingRight = 0,
                                  int marginTop = 0, int marginLeft = 0, int marginBottom = 0, int marginRight = 0 )
    {
        if( r is ContentBox b ) return b.AddPadding( paddingTop, paddingLeft, paddingBottom, paddingRight )
                                        .AddMargin( marginTop, marginLeft, marginBottom, marginRight )
                                        .WithAlign( align );
        return new ContentBox( r, paddingTop, paddingLeft, paddingBottom, paddingRight,
                                  marginTop, marginLeft, marginBottom, marginRight,
                                  align: align );
    }

    /// <summary>
    /// Applies <see cref="TextStyle"/>, padding and/or margin to this renderable by
    /// creating a new <see cref="ContentBox"/> around this renderable.
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <param name="color">Text color to apply.</param>
    /// <param name="effect">Text effect to apply.</param>
    /// <param name="paddingTop">Optional top padding.</param>
    /// <param name="paddingLeft">Optional left padding.</param>
    /// <param name="paddingBottom">Optional bottom padding.</param>
    /// <param name="paddingRight">Optional right padding.</param>
    /// <param name="marginTop">Optional top margin.</param>
    /// <param name="marginLeft">Optional left margin.</param>
    /// <param name="marginBottom">Optional bottom margin.</param>
    /// <param name="marginRight">Optional right margin.</param>
    /// <returns>A content box.</returns>
    public static ContentBox Box( this IRenderable r,
                                  Color color,
                                  TextEffect effect = TextEffect.Regular,
                                  int paddingTop = 0, int paddingLeft = 0, int paddingBottom = 0, int paddingRight = 0,
                                  int marginTop = 0, int marginLeft = 0, int marginBottom = 0, int marginRight = 0 )
    {
        return Box( r, new TextStyle( color, effect ),
                       paddingTop, paddingLeft, paddingBottom, paddingRight,
                       marginTop, marginLeft, marginBottom, marginRight );
    }

    /// <summary>
    /// Creates a <see cref="HorizontalContent"/> ending with this renderable.
    /// <para>
    /// The returned result may be this one if <paramref name="horizontalContent"/> is empty.
    /// </para>
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <param name="horizontalContent">Any number of renderables that must appear before this one.</param>
    /// <returns>The renderable.</returns>
    public static IRenderable AddLeft( this IRenderable r, params ReadOnlySpan<IRenderable?> horizontalContent )
    {
        if( horizontalContent.Length == 0 ) return r;
        if( r is HorizontalContent h ) return h.Prepend( horizontalContent );
        int flattenedLength = VerticalContent.ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return r;
        var skipThis = r.Width == 0;
        if( skipThis ) --flattenedLength;
        var a = new IRenderable[flattenedLength + 1];
        a[^1] = r;
        return HorizontalContent.FillNewContent( r.ScreenType, horizontalContent, hasSpecial, a, 0 );
    }

    /// <inheritdoc cref="AddLeft(IRenderable, ReadOnlySpan{IRenderable?})"/>
    public static IRenderable AddLeft( this IRenderable r, params IEnumerable<IRenderable?> horizontalContent ) => AddLeft( r, [.. horizontalContent] );

    /// <inheritdoc cref="AddLeft(IRenderable, ReadOnlySpan{IRenderable?})"/>
    /// <remarks>
    /// The content is automatically flattened.
    /// </remarks>
    public static IRenderable AddLeft( this IRenderable r, params IEnumerable<object?> horizontalContent ) => AddLeft( r, Flatten( horizontalContent ) );

    /// <summary>
    /// Creates a <see cref="HorizontalContent"/> starting with this renderable.
    /// <para>
    /// The returned result may be this one if <paramref name="horizontalContent"/> is empty.
    /// </para>
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <param name="horizontalContent">Any number of renderables that must appear after this one.</param>
    /// <returns>The renderable.</returns>
    public static IRenderable AddRight( this IRenderable r, params ReadOnlySpan<IRenderable?> horizontalContent )
    {
        if( horizontalContent.Length == 0 ) return r;
        if( r is HorizontalContent h ) return h.Append( horizontalContent );
        int flattenedLength = VerticalContent.ComputeActualContentLength( horizontalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return r;
        var skipThis = r.Width == 0;
        if( skipThis ) --flattenedLength;
        var a = new IRenderable[1 + flattenedLength];
        a[0] = r;
        return HorizontalContent.FillNewContent( r.ScreenType, horizontalContent, hasSpecial, a, skipThis ? 0 : 1 );
    }

    /// <inheritdoc cref="AddRight(IRenderable, ReadOnlySpan{IRenderable?})"/>
    public static IRenderable AddRight( this IRenderable r, params IEnumerable<IRenderable?> horizontalContent ) => AddRight( r, [.. horizontalContent] );

    /// <inheritdoc cref="AddRight(IRenderable, ReadOnlySpan{IRenderable?})"/>
    /// <remarks>
    /// The content is automatically flattened.
    /// </remarks>
    public static IRenderable AddRight( this IRenderable r, params IEnumerable<object?> horizontalContent ) => AddRight( r, Flatten( horizontalContent ) );

    /// <summary>
    /// Creates a <see cref="VerticalContent"/> starting with this renderable.
    /// <para>
    /// The returned result may be this one if <paramref name="verticalContent"/> is empty.
    /// </para>
    /// </summary>
    /// <param name="r">This renderable.</param>
    /// <param name="verticalContent">Any number of renderables that must appear below this one.</param>
    /// <returns>The renderable.</returns>
    public static IRenderable AddBelow( this IRenderable r, params ReadOnlySpan<IRenderable?> verticalContent )
    {
        if( verticalContent.Length == 0 ) return r;
        if( r is VerticalContent v ) return v.Append( verticalContent );
        int flattenedLength = VerticalContent.ComputeActualContentLength( verticalContent, out bool hasSpecial );
        if( flattenedLength == 0 ) return r;
        var skipThis = r.Height == 0;
        if( skipThis ) --flattenedLength;
        var a = new IRenderable[1 + flattenedLength];
        a[0] = r;
        return VerticalContent.FillNewContent( r.ScreenType, verticalContent, hasSpecial, a, skipThis ? 0 : 1 );
    }

    /// <inheritdoc cref="AddBelow(IRenderable, ReadOnlySpan{IRenderable?})"/>
    public static IRenderable AddBelow( this IRenderable r, params IEnumerable<IRenderable?> verticalContent ) => AddBelow( r, [.. verticalContent] );

    /// <inheritdoc cref="AddBelow(IRenderable, ReadOnlySpan{IRenderable?})"/>
    /// <remarks>
    /// The content is automatically flattened.
    /// </remarks>
    public static IRenderable AddBelow( this IRenderable r, params IEnumerable<object?> verticalContent ) => AddBelow( r, Flatten( verticalContent ) );

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
                Throw.ArgumentException( $"Invalid object type '{o.GetType()}' in enumeration: must be IRenderable, IEnumerable<IRenderable>, IEnumerable<object> (or null)." );
            }
        }
    }
}
