using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

/// <summary>
/// Template method that provides a <see cref="FinalStyle"/> to <see cref="Render()"/>.
/// </summary>
public class SegmentRenderer
{
    readonly SegmentRenderer? _parent;
    SegmentRenderer? _firstChild;
    SegmentRenderer? _lastChild;
    SegmentRenderer? _next;
    readonly IRenderTarget _target;
    readonly int _length;
    int _contentLength;
    TextStyle _parentStyle;
    TextStyle _style;

    // Root renderer.
    SegmentRenderer( IRenderTarget target )
    {
        _target = target;
        // The first child that specifies styles will complete it.
        _style = TextStyle.None;
        _parentStyle = TextStyle.Default;
    }

    /// <summary>
    /// Initializes a new segment renderer without content.
    /// </summary>
    /// <param name="parent">The parent renderer.</param>
    /// <param name="length">
    /// The number of characters that must be rendered. This is most often the <see cref="IRenderable.Width"/>
    /// but can be the length of a smaller line in a multi line content.
    /// </param>
    /// <param name="style">Optional text style to apply.</param>
    protected SegmentRenderer( SegmentRenderer parent, int length, TextStyle style = default )
    {
        _parent = parent;
        _target = parent._target;
        if( _parent._lastChild == null )
        {
            _parent._firstChild = this;
            _parent._lastChild = this;
        }
        else
        {
            _parent._lastChild._next = this;
            _parent._lastChild = this;
        }
        _length = length;
        parent._contentLength += length;

        // If the parent style is not fully defined, completes it with the style of this child.
        // This acts as a "lift" of the first exisitng style to, potentially, the whole renderable
        // structure...
        // Is this a good idea?
        // It is that approach or falling back to the TextStyle.Default eveywhere...
        if( !style.IgnoreAll && !parent._style.IgnoreNothing )
        {
            var s = style;
            var p = parent;
            do
            {
                s = p._style.CompleteWith( s );
                p._style = s;
                p = p._parent;
            }
            while( p != null && !p._style.IgnoreNothing );
        }
        _parentStyle = parent._style;
        _style = _parentStyle.OverrideWith( style );
    }

    /// <summary>
    /// Initializes a new segment renderer that has a content.
    /// </summary>
    /// <param name="parent">The parent renderer.</param>
    /// <param name="length">
    /// The number of characters that must be rendered. This is most often the <see cref="IRenderable.Width"/>
    /// but can be the length of a smaller line in a multi line content.
    /// </param>
    /// <param name="content">The content.</param>
    /// <param name="line">The rendered line number.</param>
    /// <param name="actualHeight">The actual height.</param>
    /// <param name="style">Optional text style to apply.</param>
    public SegmentRenderer( SegmentRenderer parent, int length, IRenderable content, int line, int actualHeight, TextStyle style = default )
        : this( parent, length, style )
    {
        content.BuildSegmentTree( line, this, actualHeight );
    }

    /// <summary>
    /// Gets the segment length.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Gets the actual content length. Padding aligns this content length with <see cref="Length"/>.
    /// </summary>
    public int ContentLength => _contentLength;

    /// <summary>
    /// Gets the final text style to apply.
    /// </summary>
    public TextStyle FinalStyle => _style;

    /// <summary>
    /// Gets the text style of the parent. Used by margins.
    /// </summary>
    public TextStyle ParentFinalStyle => _parentStyle;

    /// <summary>
    /// Rendering target.
    /// </summary>
    public IRenderTarget Target => _target;

    /// <summary>
    /// Renders the content if any.
    /// </summary>
    public void RenderContent()
    {
        var c = _firstChild;
        while( c != null )
        {
            c.Render();
            c = c._next;
        }
    }

    /// <summary>
    /// The method to implement.
    /// Default implementation calls <see cref="RenderContent()"/> that does nothing if there is no content.
    /// <para>
    /// Implementations should use <see cref="FinalStyle"/>, <see cref="Length"/>, <see cref="ContentLength"/> and
    /// <see cref="RenderContent"/> to render in <see cref="Target"/>.
    /// </para>
    /// </summary>
    /// <param name="target">The rendering target.</param>
    protected virtual void Render() => RenderContent();

    public static void Render( IRenderable r, IRenderTarget target )
    {
        for( int line = 0; line < r.Height; line++ )
        {
            RenderLine( r, target, line, r.Height );
            target.EndOfLine();
        }
    }

    static void RenderLine( IRenderable r, IRenderTarget target, int line, int height )
    {
        var head = new SegmentRenderer( target );
        r.BuildSegmentTree( line, head, height );
        head.Render();
    }
}
