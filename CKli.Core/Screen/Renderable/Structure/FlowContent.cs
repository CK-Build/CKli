using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// A flowing set of renderables: unlike a <see cref="HorizontalContent"/> - whose cells are columns that
/// share the available width - a flow is a paragraph. Its cells stay at their own width and the flow
/// breaks into as many lines as the width it is given requires.
/// <para>
/// This is what an inline list of arbitrary length (a comma separated list of repository names) must be:
/// a HorizontalContent of 60 names has a <see cref="IRenderable.MinWidth"/> of 60 times the minimal width
/// of a text, so it cannot fit any screen and every one of its cells ends up wrapped inside 10 columns.
/// A flow's minimal width is the width of its widest cell, exactly like a <see cref="VerticalContent"/>.
/// </para>
/// <para>
/// A cell is atomic: a line break never lands inside one. A group that must not be split - a name and the
/// comma that follows it - is therefore a single cell (a <see cref="HorizontalContent"/> of the two).
/// Only a cell that cannot fit a line on its own is given the available width, and it then wraps according
/// to its own rules (this is what a <see cref="TextBlock"/> does).
/// </para>
/// </summary>
public sealed class FlowContent : IRenderable
{
    readonly ImmutableArray<IRenderable> _cells;
    readonly ScreenType _screenType;
    // Both are null for a natural flow and both are set for the result of a SetWidth: _origin is the natural
    // flow this one comes from (a subsequent SetWidth flows IT rather than a flowed content: this is what
    // makes SetWidth idempotent and lets a widened screen recover a single line) and _rows is the layout.
    readonly FlowContent? _origin;
    readonly IRenderable? _rows;
    readonly int _hangingIndent;
    readonly int _width;
    readonly int _height;
    readonly int _minWidth;
    readonly int _nominalWidth;

    /// <summary>
    /// Initializes a new flow content.
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="hangingIndent">
    /// Left margin of the lines below the first one. 0 for a plain paragraph. When the first cells are a
    /// head that the flow follows, this is the head's width: the continuation lines then align under the
    /// flow instead of under the head.
    /// </param>
    /// <param name="cells">The content.</param>
    public FlowContent( ScreenType screenType, int hangingIndent, params ImmutableArray<IRenderable> cells )
    {
        Throw.CheckOutOfRangeArgument( hangingIndent >= 0 );
        _screenType = screenType;
        _cells = cells;
        _hangingIndent = hangingIndent;
        _width = ComputeWidth( cells, out _minWidth, out _nominalWidth );
        _height = _width > 0 ? ComputeHeight( cells ) : 0;
        // A single cell never flows: it has no continuation line to indent.
        if( cells.Length > 1 ) _minWidth += hangingIndent;
    }

    FlowContent( FlowContent origin, IRenderable rows )
    {
        _screenType = origin._screenType;
        _cells = origin._cells;
        _origin = origin;
        _rows = rows;
        _hangingIndent = origin._hangingIndent;
        _minWidth = origin._minWidth;
        _nominalWidth = origin._nominalWidth;
        _width = rows.Width;
        _height = rows.Height;
    }

    static int ComputeWidth( ImmutableArray<IRenderable> cells, out int minWidth, out int nominalWidth )
    {
        // The width of a flow is the width it takes on one line, but its minimal width is the widest of
        // its cells: this is the whole point of the type.
        nominalWidth = 0;
        minWidth = 0;
        int w = 0;
        foreach( var cell in cells )
        {
            w += cell.Width;
            nominalWidth += cell.NominalWidth;
            if( minWidth < cell.MinWidth ) minWidth = cell.MinWidth;
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

    /// <inheritdoc />
    public ScreenType ScreenType => _screenType;

    /// <inheritdoc />
    public int Height => _height;

    /// <inheritdoc />
    public int Width => _width;

    /// <inheritdoc />
    public int MinWidth => _minWidth;

    /// <inheritdoc />
    public int NominalWidth => _nominalWidth;

    /// <summary>
    /// Gets the content. When this flow is the result of a <see cref="SetWidth(int, bool)"/>, these are the
    /// cells of the flow it comes from: the lines it has been broken into are not exposed.
    /// </summary>
    public ImmutableArray<IRenderable> Cells => _cells;

    /// <summary>
    /// Gets the left margin of the lines below the first one.
    /// </summary>
    public int HangingIndent => _hangingIndent;

    /// <inheritdoc />
    public IRenderable SetWidth( int width, bool allowWider )
    {
        // Flowing an already flowed content would break its lines again: we always flow the natural one.
        if( _origin != null ) return _origin.SetWidth( width, allowWider );
        if( width < _minWidth ) width = _minWidth;
        // A flow is never stretched: it takes the width it needs and no more. There is nothing to
        // distribute the extra columns to - its cells are not columns.
        if( width >= _width ) return this;

        var rows = ImmutableArray.CreateBuilder<IRenderable>();
        var current = new List<IRenderable>( _cells.Length );
        int avail = width;
        int w = 0;
        foreach( var cell in _cells )
        {
            // A cell that cannot fit a line on its own is given the line: it wraps on its own.
            var c = cell.Width > avail ? cell.SetWidth( avail, false ) : cell;
            if( w > 0 && w + c.Width > avail )
            {
                rows.Add( CloseRow( _screenType, current, rows.Count > 0 ? _hangingIndent : 0 ) );
                w = 0;
                if( rows.Count == 1 && _hangingIndent > 0 )
                {
                    // From the second line on, the hanging indent eats into the available width.
                    avail = width - _hangingIndent;
                    Throw.DebugAssert( "MinWidth accounts for the hanging indent.", avail > 0 );
                    if( cell.Width > avail ) c = cell.SetWidth( avail, false );
                }
            }
            current.Add( c );
            w += c.Width;
        }
        if( current.Count > 0 )
        {
            rows.Add( CloseRow( _screenType, current, rows.Count > 0 ? _hangingIndent : 0 ) );
        }
        if( rows.Count == 0 ) return this;
        return new FlowContent( this, rows.Count == 1
                                        ? rows[0]
                                        : new VerticalContent( _screenType, rows.DrainToImmutable() ) );

        static IRenderable CloseRow( ScreenType screenType, List<IRenderable> current, int indent )
        {
            IRenderable row = current.Count == 1
                                ? current[0]
                                : new HorizontalContent( screenType, [.. current] );
            current.Clear();
            return indent > 0 ? row.Box( marginLeft: indent ) : row;
        }
    }

    /// <summary>
    /// Applies a transform function to all <see cref="Cells"/>. The result is a natural flow: a transformed
    /// cell invalidates the lines this one may have been broken into.
    /// </summary>
    /// <param name="f">The transformation to apply.</param>
    /// <returns>A new flow content, <see cref="ScreenType.Unit"/> or this if nothing changed.</returns>
    public IRenderable ApplyTransform( Func<IRenderable, IRenderable?> f )
    {
        ImmutableArray<IRenderable>.Builder? b = null;
        for( int i = 0; i < _cells.Length; i++ )
        {
            var c = _cells[i];
            var newC = f( c );
            if( newC != c )
            {
                if( b == null )
                {
                    b = ImmutableArray.CreateBuilder<IRenderable>( _cells.Length );
                    b.AddRange( _cells, i );
                }
                if( newC != null && newC.Width > 0 ) b.Add( newC );
            }
            else
            {
                b?.Add( c );
            }
        }
        return b == null
                ? this
                : b.Count == 0
                    ? _screenType.Unit
                    : new FlowContent( _screenType, _hangingIndent, b.DrainToImmutable() );
    }

    /// <inheritdoc />
    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

    /// <inheritdoc />
    public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight )
    {
        Throw.CheckArgument( line >= 0 && line < actualHeight && actualHeight >= Height );
        if( _rows != null )
        {
            if( line < _height ) _rows.BuildSegmentTree( line, parent, actualHeight );
        }
        else if( line < _height )
        {
            // Not flowed: this is a HorizontalContent.
            foreach( var cell in _cells )
            {
                cell.BuildSegmentTree( line, parent, actualHeight );
            }
        }
    }
}
