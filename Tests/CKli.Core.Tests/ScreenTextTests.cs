using NUnit.Framework;
using Shouldly;
using System;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class ScreenTextTests
{
    [Test]
    public void TextStyle_override_test()
    {
        var parentStyle = new TextStyle( ConsoleColor.DarkRed, ConsoleColor.Black, TextEffect.Regular );
        var style = new TextStyle( ConsoleColor.DarkGreen, ConsoleColor.Black, default );
        var finalStyle = parentStyle.OverrideWith( style );
        finalStyle.Color.ShouldBe( style.Color );
        finalStyle.Effect.ShouldBe( parentStyle.Effect );
    }

    [Test]
    public void TextStyle_cascading()
    {
        var rF = CreateRepo( false );
        DebugRenderer.Render( rF ).ShouldBe( """
            [DARKGREEN] Sample-Repo [GRAY]⮐

            """ );

        var rT = CreateRepo( true );
        DebugRenderer.Render( rT ).ShouldBe( """
            [DARKRED]⋆Sample-Repo [GRAY]⮐

            """ );

        static IRenderable CreateRepo( bool isDirty )
        {
            var folderStyle = new TextStyle( isDirty ? ConsoleColor.DarkRed : ConsoleColor.DarkGreen, ConsoleColor.Black );
            IRenderable folder = ScreenType.Default.Text( "Sample-Repo" ).HyperLink( new System.Uri( $"file:///C:\\Sample-Repo" ) );
            if( isDirty ) folder = folder.Box( paddingRight: 1 ).AddLeft( ScreenType.Default.Text( "⋆" ) );
            else folder = folder.Box( paddingLeft: 1, paddingRight: 1 );
            return folder.Box( style: folderStyle );
        }

    }

    [Test]
    public void Minimal_text_wrap()
    {
        var t = ScreenType.Default.Text( """


                        Some text to
                    test reduces.
                    (text is trimmed.)   




                    """ );
        TextBlock.MinimalWidth.ShouldBe( 10 );
        var tMin = t.SetWidth( 10 );
        DebugRenderer.Render( tMin ).ShouldBe( """
            Some text⮐
            to⮐
            test⮐
            reduces.⮐
            (text is⮐
            trimmed.)⮐

            """ );
    }

    [Test]
    public void ContentAlign_Horizontal_test()
    {
        var text = ScreenType.Default.Text( """
                Hello world,

                I'm glad to be there...
                But...
                I'm sad that Trump's here.
                """ );

        {
            var bUnk = text.Box( ContentAlign.Unknwon );
            var bLeft = text.Box( ContentAlign.HLeft );
            string result = """
                Hello world,··············
                ··························
                I'm glad to be there...···
                But...····················
                I'm sad that Trump's here.

                """.Replace( '·', ' ' );
            bUnk.RenderAsString().ShouldBe( result );
            bLeft.RenderAsString().ShouldBe( result );
        }
        {
            var bRight = text.Box( ContentAlign.HRight );
            bRight.RenderAsString().ShouldBe( """
                ··············Hello world,
                ··························
                ···I'm glad to be there...
                ····················But...
                I'm sad that Trump's here.

                """.Replace( '·', ' ' )
                );
        }
        {
            var bCenter = text.Box( ContentAlign.HCenter );
            bCenter.RenderAsString().ShouldBe( """
                ·······Hello world,·······
                ··························
                ·I'm glad to be there...··
                ··········But...··········
                I'm sad that Trump's here.

                """.Replace( '·', ' ' )
                );
        }
    }

    [Test]
    public void ContentAlign_Vertical_test()
    {
        var text = ScreenType.Default.Text( """
                Hello world,

                I'm glad to be there...
                But...
                I'm sad that Trump's here.
                """ );
        var bigCell = ScreenType.Default.Text( """
                |1
                |2
                |3
                |4
                |5
                |6
                |7
                |8
                |9
                |10
                """ );
        {
            var lineUnk = text.Box().AddRight( bigCell );
            var lineTop = text.Box( ContentAlign.VTop ).AddRight( bigCell );
            string result = """
                Hello world,··············|1
                ··························|2
                I'm glad to be there...···|3
                But...····················|4
                I'm sad that Trump's here.|5
                ··························|6
                ··························|7
                ··························|8
                ··························|9
                ··························|10

                """.Replace( '·', ' ' );
            lineUnk.RenderAsString().ShouldBe( result );
            lineTop.RenderAsString().ShouldBe( result );
        }
        // VBottom: bigCell (no box) - text
        {
            var lineBottom = text.Box( ContentAlign.VBottom ).AddLeft( bigCell );
            string result = """
                |1··························
                |2··························
                |3··························
                |4··························
                |5··························
                |6Hello world,··············
                |7··························
                |8I'm glad to be there...···
                |9But...····················
                |10I'm sad that Trump's here.

                """.Replace( '·', ' ' );
            lineBottom.RenderAsString().ShouldBe( result );
        }
        // VBottom: bigCell (box) - text
        {
            var lineBottom = text.Box( ContentAlign.VBottom ).AddLeft( bigCell.Box() );
            string result = """
                |1 ··························
                |2 ··························
                |3 ··························
                |4 ··························
                |5 ··························
                |6 Hello world,··············
                |7 ··························
                |8 I'm glad to be there...···
                |9 But...····················
                |10I'm sad that Trump's here.

                """.Replace( '·', ' ' );
            lineBottom.RenderAsString().ShouldBe( result );
        }
        // VBottom: bigCell (box margin right 1) - text
        {
            var lineBottom = text.Box( ContentAlign.VBottom ).AddLeft( bigCell.Box( marginRight: 1 ) );
            string result = """
                |1  ··························
                |2  ··························
                |3  ··························
                |4  ··························
                |5  ··························
                |6  Hello world,··············
                |7  ··························
                |8  I'm glad to be there...···
                |9  But...····················
                |10 I'm sad that Trump's here.

                """.Replace( '·', ' ' );
            lineBottom.RenderAsString().ShouldBe( result );
        }
        // VMiddle: bigCell (box margin right 1) - text
        {
            var lineBottom = text.Box( ContentAlign.VMiddle ).AddLeft( bigCell.Box( marginRight: 1 ) );
            string result = """
                |1  ··························
                |2  ··························
                |3  Hello world,··············
                |4  ··························
                |5  I'm glad to be there...···
                |6  But...····················
                |7  I'm sad that Trump's here.
                |8  ··························
                |9  ··························
                |10 ··························

                """.Replace( '·', ' ' );
            lineBottom.RenderAsString().ShouldBe( result );
        }
    }

    [Test]
    public void simple_line_test()
    {
        {
            var line = ScreenType.Default.Text( "Hello" ).AddRight( ScreenType.Default.Text( "world" ) );
            line.RenderAsString().ShouldBe( """
                Helloworld

                """ );
        }
        {
            var line = ScreenType.Default.Text( "Hello" ).Box( paddingLeft: 1, paddingRight: 1 ).AddRight( ScreenType.Default.Text( "world" ) );
            line.RenderAsString().ShouldBe( """
                 Hello world

                """ );
        }
        {
            var line = ScreenType.Default.Text( "Hello" ).Box( paddingLeft: 1, paddingRight: 1 )
                            .AddRight( ScreenType.Default.Text( "world" ).Box( paddingLeft: 2, paddingRight: 2 ) )
                            .AddRight( ScreenType.Default.Text( "!" ) );
            line.RenderAsString().ShouldBe( """
                 Hello   world  !

                """ );
        }
    }

    [Test]
    public void simple_2_lines_test()
    {
        {
            var line = ScreenType.Default.Text( "Message:" )
                        .AddBelow( ScreenType.Default.Text( "Hello world!" ).Box( marginLeft: 3 ) );
            line.RenderAsString().ShouldBe( """
                Message:
                   Hello world!

                """ );
        }
        {
            var line = ScreenType.Default.Text( "Message:" )
                        .AddBelow( ScreenType.Default.Text( "Hello world!" ).Box( paddingLeft: 3 ) );
            line.RenderAsString().ShouldBe( """
                Message:
                   Hello world!

                """ );
        }
        {
            var line = ScreenType.Default.Text( """


                  text
                is trimmed  


                """ );
            line.RenderAsString().ShouldBe( """
                text
                is trimmed

                """ );
        }
    }

    [Test]
    public void TextBlock_witdh_adjustement()
    {
        {
            var t = ScreenType.Default.Text( "            0 1 2 3 4 5 6 7 8 9 A B C D E F G H I J K L M N O P Q R S T U V W X Y Z               " );
            var tMin = t.SetWidth( 15 );
            tMin.Width.ShouldBe( 15 );
            tMin.Height.ShouldBe( 5 );
            tMin.RenderAsString().ShouldBe( """
                0 1 2 3 4 5 6 7
                8 9 A B C D E F
                G H I J K L M N
                O P Q R S T U V
                W X Y Z

                """ );
        }
        {
            var t = ScreenType.Default.Text( "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ" );
            var tMin = t.SetWidth( 15 );
            tMin.Width.ShouldBe( 15 );
            tMin.Height.ShouldBe( 3 );
            tMin.RenderAsString().ShouldBe( """
                0123456789ABCDE
                FGHIJKLMNOPQRST
                UVWXYZ

                """ );
        }
        { 
            var t = ScreenType.Default.Text( "La liberté des uns s'arrête où commence celle des autres." );
            var tMin = t.SetWidth( 15 );
            tMin.Width.ShouldBe( 15 );
            tMin.Height.ShouldBe( 4 );
            tMin.RenderAsString().ShouldBe( """
                La liberté des
                uns s'arrête où
                commence celle
                des autres.

                """ );
            var tMin1 = t.SetWidth( 15 + 1 );
            tMin1.RenderAsString().ShouldBe( tMin.RenderAsString() );
            var tMin2 = tMin1.SetWidth( 15 + 2 );
            tMin2.RenderAsString().ShouldBe( tMin.RenderAsString() );
            var tMin3 = tMin2.SetWidth( 15 + 3 );
            tMin3.Height.ShouldBe( 4 );
            tMin3.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où
                commence celle des
                autres.

                """ );
            var tMin10 = tMin2.SetWidth( 15 + 10 );
            tMin10.Height.ShouldBe( 3 );
            tMin10.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où commence
                celle des autres.

                """ );

            var tMin11 = tMin2.SetWidth( 15 + 11 );
            tMin11.Height.ShouldBe( 3 );
            tMin11.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où commence celle
                des autres.

                """ );
        }
        {
            var t = ScreenType.Default.Text( """
                                A
                                B            

                                C       


                """ );
            var tMin = t.SetWidth( 15 );
            tMin.ShouldBeSameAs( t );
            t.RenderAsString().ShouldBe( """
                A
                B

                C

                """ );

        }

    }

    [Test]
    public void ContentBox_small_witdh_adjustement()
    {
        var b = ScreenType.Default.Text( """
            1
            2
            """ ).Box();
        {
            b = b.SetWidth( 15 );
            b.Margin.Right.ShouldBe( 14 );
            b.RenderAsString().ShouldBe( """
                1··············
                2··············

                """.Replace( '·', ' ' ) );
        }
        {
            b = b.WithAlign( ContentAlign.HRight ).SetWidth( 15 );
            b.Margin.Left.ShouldBe( 14 );
            b.RenderAsString().ShouldBe( """
                ··············1
                ··············2

                """.Replace( '·', ' ' ) );
        }
        var bRight = b;
        {
            b = bRight.WithAlign( ContentAlign.HCenter ).SetWidth( 15 );
            b.Margin.Left.ShouldBe( 7 );
            b.Margin.Right.ShouldBe( 7 );
            b.RenderAsString().ShouldBe( """
                ·······1·······
                ·······2·······

                """.Replace( '·', ' ' ) );
        }
        {
            b = b.SetWidth( 16 );
            b.Margin.Left.ShouldBe( 7 );
            b.Margin.Right.ShouldBe( 8 );
            b.RenderAsString().ShouldBe( """
                ·······1········
                ·······2········

                """.Replace( '·', ' ' ) );
        }
        {
            b = bRight.SetWidth( 16 ).WithAlign( ContentAlign.HCenter );
            b.Margin.Left.ShouldBe( 7 );
            b.Margin.Right.ShouldBe( 8 );
            b.RenderAsString().ShouldBe( """
                ·······1········
                ·······2········

                """.Replace( '·', ' ' ) );
        }
    }

    [Test]
    public void ContentBox_regular_witdh_adjustement()
    {
        TextBlock.MinimalWidth.ShouldBeLessThan( 16 );
        var text = "0123456789ABCDEF";
        var b = ScreenType.Default.Text( text ).Box();
        {
            b = b.WithAlign( ContentAlign.HCenter ).SetWidth( 21 );
            b.RenderAsString().ShouldBe( $"""
                ··{text}···

                """.Replace( '·', ' ' ) );
        }
        {
            b = b.WithAlign( ContentAlign.HRight );
            b.RenderAsString().ShouldBe( $"""
                ·····{text}

                """.Replace( '·', ' ' ) );
        }
        {
            b = b.WithAlign( ContentAlign.HLeft );
            b.RenderAsString().ShouldBe( $"""
                {text}·····

                """.Replace( '·', ' ' ) );
        }
        {
            b = b.SetWidth( 15 );
            b.RenderAsString().ShouldBe( $"""
                0123456789ABCDE
                F··············

                """.Replace( '·', ' ' ) );
        }
        {
            b = b.WithAlign( ContentAlign.HCenter );
            b.RenderAsString().ShouldBe( $"""
                0123456789ABCDE
                ·······F·······

                """.Replace( '·', ' ' ) );
        }
        {
            b = b.SetWidth( 14 );
            b.RenderAsString().ShouldBe( $"""
                0123456789ABCD
                ······EF······

                """.Replace( '·', ' ' ) );
        }
        TextBlock.MinimalWidth.ShouldBe( 10 );
        {
            b = b.SetWidth( TextBlock.MinimalWidth );
            b.RenderAsString().ShouldBe( $"""
                0123456789
                ··ABCDEF··

                """.Replace( '·', ' ' ) );
        }
        {
            var same = b.SetWidth( 2 );
            b.ShouldBeSameAs( same );
        }
        {
            var same = b.SetWidth( 0 );
            b.ShouldBeSameAs( same );
        }
        {
            var same = b.SetWidth( -1 );
            b.ShouldBeSameAs( same );
        }
    }

    [Test]
    public void ContentBox_width_Adjustement()
    {
        var b = ScreenType.Default.Text( "X", new TextStyle( ConsoleColor.Red, ConsoleColor.Black ) )
                                        .Box( marginLeft: 2, paddingLeft: 3, style: new TextStyle( ConsoleColor.Green, ConsoleColor.Black ) );
        {
            DebugRenderer.Render( b ).ShouldBe( """
                  [GREEN]   [RED]X[GRAY]⮐

                """ );
        }
        {
            b = b.SetWidth( 15 );
            DebugRenderer.Render( b ).ShouldBe( """
                  [GREEN]   [RED]X[GRAY]         ⮐

                """ );
        }
        {
            b = b.AddPadding( right: 3 );
            b.Width.ShouldBe( 15 );
            DebugRenderer.Render( b ).ShouldBe( """
                  [GREEN]   [RED]X[GREEN]   [GRAY]      ⮐
                
                """ );
        }
        {
            b = b.AddPadding( right: 5 );
            DebugRenderer.Render( b ).ShouldBe( """
                  [GREEN]   [RED]X[GREEN]        [GRAY] ⮐

                """ );
        }
        {
            b = b.AddPadding( right: -5 );
            DebugRenderer.Render( b ).ShouldBe( """
                  [GREEN]   [RED]X[GREEN]   [GRAY]      ⮐

                """ );
        }
        {
            // MinWidth: Only one right and left padding is preserved.
            b.Margin.Left.ShouldBe( 2 );
            b.Padding.Left.ShouldBe( 3 );
            b.Padding.Right.ShouldBe( 3 );
            b.Margin.Right.ShouldBe( 6, "Width adjustment." );
            b.MinWidth.ShouldBe( 1 + 1 + 1 );
            b.NominalWidth.ShouldBe( 2 + 3 + 1 + 3 + 0 );
            b = b.SetWidth( 0 );
            b.Width.ShouldBe( 3 );
            DebugRenderer.Render( b ).ShouldBe( """
                [GREEN] [RED]X[GREEN] [GRAY]⮐

                """ );
        }
        {
            // MinWidth + 1: Right padding is 2 (because the ContentAlignment is HLeft).
            b = b.SetWidth( 4 );
            b.Width.ShouldBe( 4 );
            DebugRenderer.Render( b ).ShouldBe( """
                [GREEN] [RED]X[GREEN]  [GRAY]⮐

                """ );
        }
        {
            // MinWidth + 2: Left and Right padding are 2.
            b = b.SetWidth( 5 );
            b.Width.ShouldBe( 5 );
            DebugRenderer.Render( b ).ShouldBe( """
                [GREEN]  [RED]X[GREEN]  [GRAY]⮐

                """ );
        }
        {
            // MinWidth + 3: Left padding stays to 2 and Right padding is 3.
            b = b.SetWidth( 6 );
            b.Width.ShouldBe( 6 );
            DebugRenderer.Render( b ).ShouldBe( """
                [GREEN]  [RED]X[GREEN]   [GRAY]⮐

                """ );
        }
        {
            // MinWidth + 4: Left and Right padding are restored to their 3 value.
            b = b.SetWidth( 7 );
            b.Width.ShouldBe( 7 );
            DebugRenderer.Render( b ).ShouldBe( """
                [GREEN]   [RED]X[GREEN]   [GRAY]⮐

                """ );
        }
        {
            // MinWidth + 5: Entering the margin (Left because there's no right margin here).
            b = b.SetWidth( 8 );
            b.Width.ShouldBe( 8 );
            DebugRenderer.Render( b ).ShouldBe( """
                 [GREEN]   [RED]X[GREEN]   [GRAY]⮐

                """ );
        }
        {
            // MinWidth + 6: Left margin is 2. Everything is restored.
            b = b.SetWidth( 9 );
            b.Width.ShouldBe( 9 );
            DebugRenderer.Render( b ).ShouldBe( """
                  [GREEN]   [RED]X[GREEN]   [GRAY]⮐

                """ );
        }
        {
            // MinWidth + 7: Right margin adjustment (because HLeft).
            b = b.SetWidth( 10 );
            b.Width.ShouldBe( 10 );
            DebugRenderer.Render( b ).ShouldBe( """
                  [GREEN]   [RED]X[GREEN]   [GRAY] ⮐

                """ );
        }
        {
            // MinWidth + 8: Right margin adjustment (because HLeft).
            b = b.SetWidth( 11 );
            b.Width.ShouldBe( 11 );
            DebugRenderer.Render( b ).ShouldBe( """
                  [GREEN]   [RED]X[GREEN]   [GRAY]  ⮐

                """ );
        }
    }

    [TestCase( ContentAlign.HLeft )]
    [TestCase( ContentAlign.HCenter )]
    public void ContentBox_SmallerAdjustement_2( ContentAlign a )
    {
        var b = ScreenType.Default.Text( """
                                               A
                                               BC
                                               """, new TextStyle( ConsoleColor.Red, ConsoleColor.Black ) )
                                .Box( marginLeft: 1, paddingLeft: 2, paddingRight: 3, marginRight: 4,
                                      style: new TextStyle( ConsoleColor.Green, ConsoleColor.Black ),
                                      align: a );

        {
            b.Width.ShouldBe( 1 + 2 + 2 + 3 + 4, "12" );
            b.NominalWidth.ShouldBe( 12 );
            b.MinWidth.ShouldBe( 1 + 2 + 1, "Preserves a 1 padding on both sides. Cancelling the margins." );
            DebugRenderer.Render( b ).ShouldBe( """
                 [GREEN]  [RED]A[GREEN]    [GRAY]    ⮐
                 [GREEN]  [RED]BC[GREEN]   [GRAY]    ⮐
                
                """ );
        }
        {
            // Shrinked 1: Right is chosen because ContentAligmnent is HLeft.
            var s = b.SetWidth( 11 );
            DebugRenderer.Render( s ).ShouldBe( """
                 [GREEN]  [RED]A[GREEN]    [GRAY]   ⮐
                 [GREEN]  [RED]BC[GREEN]   [GRAY]   ⮐
                
                """ );
        }
        {
            // Shrinked 2: Because we have a left padding, we can sacrifice the Left margin.
            var s = b.SetWidth( 10 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]  [RED]A[GREEN]    [GRAY]   ⮐
                [GREEN]  [RED]BC[GREEN]   [GRAY]   ⮐
                
                """ );
        }
        {
            // Shrinked 3: Right margin decreases.
            var s = b.SetWidth( 9 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]  [RED]A[GREEN]    [GRAY]  ⮐
                [GREEN]  [RED]BC[GREEN]   [GRAY]  ⮐
                
                """ );
        }
        {
            // Shrinked 4: Right margin decreases.
            var s = b.SetWidth( 8 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]  [RED]A[GREEN]    [GRAY] ⮐
                [GREEN]  [RED]BC[GREEN]   [GRAY] ⮐
                
                """ );
        }
        {
            // Shrinked 5: Right margin decreases. No more margin.
            var s = b.SetWidth( 7 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]  [RED]A[GREEN]    [GRAY]⮐
                [GREEN]  [RED]BC[GREEN]   [GRAY]⮐
                
                """ );
        }
        {
            // Shrinked 6: Starting padding (Right because HLeft).
            var s = b.SetWidth( 6 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]  [RED]A[GREEN]   [GRAY]⮐
                [GREEN]  [RED]BC[GREEN]  [GRAY]⮐
                
                """ );
        }
        {
            // Shrinked 7: Left padding is touched.
            var s = b.SetWidth( 5 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN] [RED]A[GREEN]   [GRAY]⮐
                [GREEN] [RED]BC[GREEN]  [GRAY]⮐
                
                """ );
        }
        {
            // Shrinked 8: Left and Right padding are 1: we reached the MinWidth.
            var s = b.SetWidth( 4 );
            s.Width.ShouldBe( b.MinWidth );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN] [RED]A[GREEN]  [GRAY]⮐
                [GREEN] [RED]BC[GREEN] [GRAY]⮐
                
                """ );
            var below = b.SetWidth( 0 );
            below.Width.ShouldBe( b.MinWidth );
            DebugRenderer.Render( below ).ShouldBe( """
                [GREEN] [RED]A[GREEN]  [GRAY]⮐
                [GREEN] [RED]BC[GREEN] [GRAY]⮐
                
                """ );
        }
    }

    [Test]
    public void ContentBox_SmallerAdjustement_3()
    {
        var b = ScreenType.Default.Text( """
                                               A
                                               BC
                                               """, new TextStyle( ConsoleColor.Red, ConsoleColor.Black ) )
                                .Box( marginLeft: 1, marginRight: 1,
                                      style: new TextStyle( ConsoleColor.Green, ConsoleColor.Black ) );

        {
            b.Width.ShouldBe( 1 + 0 + 2 + 0 + 1, "4" );
            b.NominalWidth.ShouldBe( 4 );
            b.MinWidth.ShouldBe( 1 + 2 + 1, "Smaller lines are padded. The one margin is preserved." );
            DebugRenderer.Render( b ).ShouldBe( """
                 [RED]A[GREEN] [GRAY] ⮐
                 [RED]BC[GRAY] ⮐
                
                """ );
        }
        {
            // Shrinked 1: no way. MiniWidth.
            var s = b.SetWidth( 3 );
            s.Width.ShouldBe( 4 );
            DebugRenderer.Render( s ).ShouldBe( """
                 [RED]A[GREEN] [GRAY] ⮐
                 [RED]BC[GRAY] ⮐
                
                """ );
        }
    }

    [Test]
    public void ContentBox_SmallerAdjustement_2_HRight()
    {
        var b = ScreenType.Default.Text( """
                                               A
                                               BC
                                               """, new TextStyle( ConsoleColor.Red, ConsoleColor.Black ) )
                                .Box( marginLeft: 1, paddingLeft: 2, paddingRight: 3, marginRight: 4,
                                      align: ContentAlign.HRight,
                                      style: new TextStyle( ConsoleColor.Green, ConsoleColor.Black ) );

        {
            b.Width.ShouldBe( 1 + 2 + 2 + 3 + 4, "12" );
            b.NominalWidth.ShouldBe( 12 );
            b.MinWidth.ShouldBe( 1 + 2 + 1, "Preserves a 1 padding on both sides. Cancelling the margins." );
            DebugRenderer.Render( b ).ShouldBe( """
                 [GREEN]   [RED]A[GREEN]   [GRAY]    ⮐
                 [GREEN]  [RED]BC[GREEN]   [GRAY]    ⮐
                
                """ );
        }
        {
            // Shrinked 1: Right margin decreases.
            var s = b.SetWidth( 11 );
            DebugRenderer.Render( s ).ShouldBe( """
                 [GREEN]   [RED]A[GREEN]   [GRAY]   ⮐
                 [GREEN]  [RED]BC[GREEN]   [GRAY]   ⮐
                
                """ );
        }
        {
            // Shrinked 2: No more Left margin.
            var s = b.SetWidth( 10 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]   [RED]A[GREEN]   [GRAY]   ⮐
                [GREEN]  [RED]BC[GREEN]   [GRAY]   ⮐
                
                """ );
        }
        {
            // Shrinked 3: Right margin again.
            var s = b.SetWidth( 9 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]   [RED]A[GREEN]   [GRAY]  ⮐
                [GREEN]  [RED]BC[GREEN]   [GRAY]  ⮐
                
                """ );
        }
        {
            // Shrinked 4: Right margin.
            var s = b.SetWidth( 8 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]   [RED]A[GREEN]   [GRAY] ⮐
                [GREEN]  [RED]BC[GREEN]   [GRAY] ⮐
                
                """ );
        }
        {
            // Shrinked 5: No more margin.
            var s = b.SetWidth( 7 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]   [RED]A[GREEN]   [GRAY]⮐
                [GREEN]  [RED]BC[GREEN]   [GRAY]⮐
                
                """ );
        }
        {
            // Shrinked 6: Starting padding.
            var s = b.SetWidth( 6 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]   [RED]A[GREEN]  [GRAY]⮐
                [GREEN]  [RED]BC[GREEN]  [GRAY]⮐
                
                """ );
        }
        {
            // Shrinked 7: Left padding is touched.
            var s = b.SetWidth( 5 );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]  [RED]A[GREEN]  [GRAY]⮐
                [GREEN] [RED]BC[GREEN]  [GRAY]⮐
                
                """ );
        }
        {
            // Shrinked 8: Left and Right padding are 1: we reached the MinWidth.
            var s = b.SetWidth( 4 );
            s.Width.ShouldBe( b.MinWidth );
            DebugRenderer.Render( s ).ShouldBe( """
                [GREEN]  [RED]A[GREEN] [GRAY]⮐
                [GREEN] [RED]BC[GREEN] [GRAY]⮐
                
                """ );
            var below = b.SetWidth( 0 );
            below.Width.ShouldBe( b.MinWidth );
            DebugRenderer.Render( below ).ShouldBe( """
                [GREEN]  [RED]A[GREEN] [GRAY]⮐
                [GREEN] [RED]BC[GREEN] [GRAY]⮐
                
                """ );
        }
    }


    [Test]
    public void CommandLineArguments_remaining_args()
    {
        var cmdLine = new CommandLineArguments( [ "some", "a",
                                                  "-fX",
                                                  "-f1",
                                                  "-o1", "O1",
                                                  "alien",
                                                  "-om", "OM1", "OM2",
                                                  "-os", "OS",
                                                  "--fY",
                                                  "-o1", "O1TOOmuch",
                                                  "alien",
                                                  "-om", "OM3",
                                                  "-unk" ] );
        cmdLine.EatArgument().ShouldBe( "some" );
        cmdLine.EatArgument().ShouldBe( "a" );
        cmdLine.EatSingleOption( "-os" ).ShouldBe( "OS" );
        cmdLine.EatSingleOption( "-o1" ).ShouldBe( "O1" );
        cmdLine.EatMultipleOption( "-om" ).ShouldBe( ["OM1", "OM2", "OM3"] );
        cmdLine.EatFlag( "-f1" ).ShouldBeTrue();

        cmdLine.Close( TestHelper.Monitor ).ShouldBe( false );
        var header = ScreenExtensions.CreateDisplayHelpHeader( ScreenType.Default, cmdLine );
        string result = header.RenderAsString();

        Console.Write( result );
        result.ShouldBe( """
                    Arguments: some a -fX -f1 -o1 O1 alien -om OM1 OM2 -os OS --fY -o1 O1TOOmuch alien -om OM3 -unk
                                      └─┘            └───┘                    └──┘ └─┘ └───────┘ └───┘         └──┘

                    
                    """ );

    }

}
