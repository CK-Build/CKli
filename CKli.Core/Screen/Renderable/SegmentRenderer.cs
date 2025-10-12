using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

public class SegmentRenderer
{
    readonly SegmentRenderer? _prev;
    readonly IRenderTarget _target;
    readonly int _length;
    SegmentRenderer? _next;
    TextStyle _style;
    bool _isRendered;

    // Root renderer.
    SegmentRenderer( IRenderTarget target, int length, TextStyle style )
    {
        _target = target;
        _length = length;
        _style = style;
    }

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

    internal SegmentRenderer CreateRendererGroup( int length, ImmutableArray<IRenderable> cells, int line ) => new RendererGroup( _target, length, _style, cells, line );

    sealed class RendererGroup : SegmentRenderer
    {
        public RendererGroup( IRenderTarget target, int length, TextStyle style, ImmutableArray<IRenderable> cells, int line )
            : base( target, length, style )
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

    protected virtual void Render( IRenderTarget target )
    {
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
        var head = new SegmentRenderer( target, 0, TextStyle.None );
        r.CollectRenderer( line, head );
        var s = head.Next;
        while( s != null )
        {
            s.Render();
            s = s._next;
        }
    }
}
