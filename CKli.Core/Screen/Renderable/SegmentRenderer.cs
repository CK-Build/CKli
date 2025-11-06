using System.Collections.Generic;

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
        _style = TextStyle.Default;
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

    internal static void Render( IEnumerable<IRenderable> renderables, IRenderTarget target, bool newLine )
    {
        target.BeginUpdate();
        var e = renderables.GetEnumerator();
        if( e.MoveNext() )
        {
            bool hasMore;
            do
            {
                var r = e.Current;
                hasMore = e.MoveNext();
                for( int line = 0; line < r.Height; line++ )
                {
                    RenderLine( r, target, line, r.Height );
                    target.EndOfLine( hasMore || newLine || line < r.Height - 1 );
                }
            }
            while( !hasMore );
        }
        target.EndUpdate();
    }

    internal static void Render( IRenderable r, IRenderTarget target, bool newLine )
    {
        target.BeginUpdate();
        for( int line = 0; line < r.Height; line++ )
        {
            RenderLine( r, target, line, r.Height );
            target.EndOfLine( newLine || line < r.Height - 1 );
        }
        target.EndUpdate();
    }

    static void RenderLine( IRenderable r, IRenderTarget target, int line, int height )
    {
        var head = new SegmentRenderer( target );
        r.BuildSegmentTree( line, head, height );
        head.Render();
    }
}
