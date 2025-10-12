using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

/// <summary>
/// Template method that provides a <see cref="FinalStyle"/> to <see cref="Render(IRenderTarget)"/>.
/// </summary>
public class SegmentRenderer
{
    readonly SegmentRenderer? _prev;
    readonly IRenderTarget _target;
    readonly int _length;
    SegmentRenderer? _next;
    TextStyle _style;
    bool _isRendered;

    // Root renderer.
    SegmentRenderer( IRenderTarget target, TextStyle style )
    {
        _target = target;
        _style = style;
    }

    /// <summary>
    /// Initializes a new segment renderer.
    /// </summary>
    /// <param name="previous">The previous renderer.</param>
    /// <param name="length">The number of characters that must be rendered (most often the <see cref="IRenderable.Width"/>).</param>
    /// <param name="style">The text style to apply.</param>
    protected SegmentRenderer( SegmentRenderer previous, int length, TextStyle style = default )
    {
        _prev = previous;
        previous._next = this;
        _target = previous._target;
        _length = length;

        if( !style.IgnoreAll && !previous._style.IgnoreNothing )
        {
            var s = style;
            var p = previous;
            do
            {
                s = p._style.CompleteWith( s );
                p._style = s;
                p = p._prev;
            }
            while( p != null && !p._style.IgnoreNothing );
        }
        _style = previous._style.OverrideWith( style );
    }

    [MemberNotNullWhen( false, nameof(_prev) )]
    bool IsRoot => _prev == null;

    /// <summary>
    /// Gets the segment length.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Gets the final text style to apply.
    /// </summary>
    public TextStyle FinalStyle => _style;

    /// <summary>
    /// Gets the next segment.
    /// </summary>
    public SegmentRenderer? Next => _next;

    /// <summary>
    /// Gets whether this segment has already been rendered.
    /// When true, <see cref="Render"/> is a no-op.
    /// </summary>
    public bool IsRendered => _isRendered;

    /// <summary>
    /// Renders this segment. A segment can be rendered once and only once, only
    /// the first call calls the protected <see cref="Render(IRenderTarget)"/>, subsequent
    /// calls do nothing.
    /// </summary>
    public void Render()
    {
        if( !_isRendered  )
        {
            _isRendered = true;
            Render( _target );
        }
    }

    /// <summary>
    /// The method to implement.
    /// Default implementation does nothing: <see cref="Empty(SegmentRenderer, TextStyle)"/> uses this.
    /// <para>
    /// Implementations should use <see cref="FinalStyle"/>, <see cref="Length"/> and <see cref="Next"/>.
    /// </para>
    /// </summary>
    /// <param name="target">The rendering target.</param>
    protected virtual void Render( IRenderTarget target )
    {
    }


    /// <summary>
    /// Creates a group of renderers. Used by <see cref="HorizontalContent"/> but can be used by more complex layout.
    /// </summary>
    /// <param name="cells">Renderables to encapsulate.</param>
    /// <param name="line">The line number to render.</param>
    /// <returns>A group of renderer.</returns>
    public SegmentRenderer CreateRendererGroup( ImmutableArray<IRenderable> cells, int line ) => new RendererGroup( _target, _style, cells, line );

    sealed class RendererGroup : SegmentRenderer
    {
        public RendererGroup( IRenderTarget target, TextStyle style, ImmutableArray<IRenderable> cells, int line )
            : base( target, style )
        {
            SegmentRenderer head = this;
            foreach( var cell in cells )
            {
                head = cell.CollectRenderer( line, head );
            }
        }

        protected override void Render( IRenderTarget target )
        {
            var s = Next;
            while( s != null )
            {
                s.Render();
                s = s.Next;
            }
        }
    }

    /// <summary>
    /// Creates an empty segment. Used by empty string or asa pure text style setter.
    /// </summary>
    /// <param name="previous">The previous segment.</param>
    /// <param name="style">Optional style.</param>
    /// <returns>The empty segment renderer.</returns>
    public static SegmentRenderer Empty( SegmentRenderer previous, TextStyle style = default ) => new SegmentRenderer( previous, 0, style );

    public static void Render( IRenderable r, IRenderTarget target )
    {
        for( int line = 0; line < r.Height; line++ )
        {
            RenderLine( r, target, line );
            target.EndOfLine();
        }
    }

    static void RenderLine( IRenderable r, IRenderTarget target, int line )
    {
        var head = new SegmentRenderer( target, TextStyle.None );
        r.CollectRenderer( line, head );
        var s = head.Next;
        while( s != null )
        {
            s.Render();
            s = s._next;
        }
    }
}
