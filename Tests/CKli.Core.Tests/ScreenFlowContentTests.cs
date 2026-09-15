using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CKli.Core.Tests;

[TestFixture]
public class ScreenFlowContentTests
{
    [Test]
    public void FlowContent_MinWidth_is_the_widest_cell_where_a_HorizontalContent_sums_them()
    {
        var cells = CreateList( 6 );
        int sum = cells.Sum( c => c.MinWidth );
        int widest = cells.Max( c => c.MinWidth );
        widest.ShouldBeLessThan( sum );
        // This is the whole point of the type: a row of N names cannot be narrower than the sum of their
        // minimal widths, so past a dozen names no screen can hold it and every cell ends up wrapped
        // inside TextBlock.MinimalWidth columns.
        var h = new HorizontalContent( ScreenType.Default, cells );
        h.MinWidth.ShouldBe( sum );

        var f = ScreenType.Default.Flow( 0, cells );
        f.MinWidth.ShouldBe( widest, "The widest cell's minimal width, like a VerticalContent." );
        f.Width.ShouldBe( h.Width, "Unconstrained, a flow is the same single line." );
        f.Height.ShouldBe( 1 );
    }

    [Test]
    public void FlowContent_MinWidth_accounts_for_the_hanging_indent()
    {
        var cells = CreateList( 6 );
        int widest = cells.Max( c => c.MinWidth );
        ScreenType.Default.Flow( 4, cells ).MinWidth.ShouldBe( 4 + widest );
        // A single cell has no continuation line to indent.
        ScreenType.Default.Flow( 4, [cells[0]] ).MinWidth.ShouldBe( cells[0].MinWidth );
    }

    [Test]
    public void FlowContent_breaks_into_lines()
    {
        var f = ScreenType.Default.Flow( 0, CreateList( 6 ) );
        DebugRenderer.Render( f ).ShouldBe( """
            Name-0, Name-1, Name-2, Name-3, Name-4, Name-5⮐

            """ );
        DebugRenderer.Render( f.SetWidth( 30, false ) ).ShouldBe( """
            Name-0, Name-1, Name-2, ⮐
            Name-3, Name-4, Name-5⮐

            """ );
        DebugRenderer.Render( f.SetWidth( 20, false ) ).ShouldBe( """
            Name-0, Name-1, ⮐
            Name-2, Name-3, ⮐
            Name-4, Name-5⮐

            """ );
    }

    [Test]
    public void FlowContent_continuation_lines_are_indented_under_the_head()
    {
        // The head is a cell of the flow and the hanging indent is its width: the names then form one
        // straight column instead of the continuation lines starting back at the left.
        var head = ScreenType.Default.Text( "v1.0.0" ).Box( marginRight: 2 );
        var f = ScreenType.Default.Flow( head.Width, [head, .. CreateList( 5 )] );
        head.Width.ShouldBe( 8 );
        DebugRenderer.Render( f.SetWidth( 34, false ) ).ShouldBe( """
            v1.0.0  Name-0, Name-1, Name-2, ⮐
                    Name-3, Name-4⮐

            """ );
    }

    [Test]
    public void FlowContent_SetWidth_always_flows_the_original()
    {
        var f = ScreenType.Default.Flow( 0, CreateList( 6 ) );
        var single = DebugRenderer.Render( f );
        var narrow = f.SetWidth( 20, false );
        narrow.Height.ShouldBeGreaterThan( 1 );
        // Widening back recovers the single line: a flowed content flows its origin, never its own lines.
        // This is what an interactive screen does on every refresh.
        DebugRenderer.Render( narrow.SetWidth( 200, false ) ).ShouldBe( single );
        DebugRenderer.Render( narrow.SetWidth( 20, false ) ).ShouldBe( DebugRenderer.Render( narrow ) );
    }

    [Test]
    public void FlowContent_gives_a_line_to_a_cell_that_cannot_fit_it()
    {
        var f = ScreenType.Default.Flow( 0, [ScreenType.Default.Text( "Short," ),
                                             ScreenType.Default.Text( "A cell far too wide for this line." )] );
        DebugRenderer.Render( f.SetWidth( 15, false ) ).ShouldBe( """
            Short,⮐
            A cell far too⮐
            wide for this⮐
            line.⮐

            """ );
    }

    [Test]
    public void FlowContent_never_breaks_inside_a_cell_group()
    {
        // A name and the comma that follows it are one cell: this is what keeps a separator from opening
        // a line. Rendering at every width from the minimal one up must never start a line with a comma.
        var f = ScreenType.Default.Flow( 0, CreateList( 8 ) );
        for( int w = f.MinWidth; w <= f.Width; w++ )
        {
            var lines = DebugRenderer.Render( f.SetWidth( w, false ) ).Split( '\n' );
            foreach( var line in lines )
            {
                line.TrimStart().ShouldNotStartWith( ",", customMessage: $"Width {w}." );
            }
        }
    }

    [TestCase( 60 )]
    [TestCase( 100 )]
    [TestCase( 160 )]
    public void A_long_inline_list_fits_the_screen( int screenWidth )
    {
        // This is the "ckli deps update" report's shape: a VerticalContent of rows, the long ones boxed
        // with a left margin. Both enclosing renderables clamp the width they are given UP to their
        // content's MinWidth, so a row whose MinWidth is the sum of 40 names never sees the screen width
        // at all: this is what made every name wrap inside 10 columns.
        var screen = ScreenType.Default;
        var head = screen.Text( "v1.0.0" ).Box( marginRight: 2 );
        var names = CreateList( 40 );

        var asRow = screen.Unit.AddBelow( screen.Text( "Dependency upgrades:" ),
                                          new HorizontalContent( screen, [head, .. names] ).Box( marginLeft: 4 ) );
        asRow.SetWidth( screenWidth, false ).Width.ShouldBeGreaterThan( screenWidth, "The bug: a row of columns cannot fit." );

        var asFlow = screen.Unit.AddBelow( screen.Text( "Dependency upgrades:" ),
                                           screen.Flow( head.Width, [head, .. names] ).Box( marginLeft: 4 ) );
        var sized = asFlow.SetWidth( screenWidth, false );
        sized.Width.ShouldBeLessThanOrEqualTo( screenWidth );
        foreach( var line in DebugRenderer.Render( sized ).Split( '\n' ) )
        {
            line.TrimEnd( '⮐', '\r' ).Length.ShouldBeLessThanOrEqualTo( screenWidth );
        }
    }

    // "Name-0," … "Name-N" where each name carries the separator that follows it: one atomic cell.
    static ImmutableArray<IRenderable> CreateList( int count )
    {
        var cells = new List<IRenderable>();
        for( int i = 0; i < count; i++ )
        {
            IRenderable c = ScreenType.Default.Text( $"Name-{i}" );
            if( i < count - 1 ) c = c.AddRight( ScreenType.Default.Text( "," ).Box( marginRight: 1 ) );
            cells.Add( c );
        }
        return [.. cells];
    }
}
