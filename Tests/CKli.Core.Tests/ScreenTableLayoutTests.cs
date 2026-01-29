using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Threading.Tasks;

namespace CKli.Core.Tests;

[TestFixture]
public class ScreenTableLayoutTests
{
    [Test]
    public void HorizontalContent_SetWidth_1()
    {
        var h = ScreenType.Default.Text( "1" ).Box()
                .AddRight( ScreenType.Default.Text( "2" ).Box( marginLeft: 1 ) )
                .AddRight( ScreenType.Default.Text( "3" ).Box( marginLeft: 1 ) );
        h.Width.ShouldBe( h.NominalWidth ).ShouldBe( h.MinWidth ).ShouldBe( 5 );
        {
            DebugRenderer.Render( h ).ShouldBe( """
                1 2 3⮐

                """ );
        }
        {
            var s = h.SetWidth( 6 );
            DebugRenderer.Render( s ).ShouldBe( """
                1 2 3 ⮐

                """ );
        }
        {
            var s = h.SetWidth( 7 );
            DebugRenderer.Render( s ).ShouldBe( """
                1 2  3 ⮐

                """ );
        }
        {
            var s = h.SetWidth( 8 );
            DebugRenderer.Render( s ).ShouldBe( """
                1  2  3 ⮐

                """ );
        }
        {
            var s = h.SetWidth( 9 );
            DebugRenderer.Render( s ).ShouldBe( """
                1  2   3 ⮐

                """ );
        }
        {
            var s = h.SetWidth( 10 );
            DebugRenderer.Render( s ).ShouldBe( """
                1  2   3  ⮐

                """ );
        }
        {
            var s = h.SetWidth( 11 );
            DebugRenderer.Render( s ).ShouldBe( """
                1  2   3   ⮐

                """ );
        }
        {
            var s = h.SetWidth( 12 );
            DebugRenderer.Render( s ).ShouldBe( """
                1  2    3   ⮐

                """ );
        }
    }

    [Test]
    public void HorizontalContent_SetWidth_2()
    {
        var h = ScreenType.Default.Text( "0123456789" ).Box( paddingLeft: 2, paddingRight: 2, marginRight: 1 )
                .AddRight( ScreenType.Default.Text( "0123456789" ).Box( paddingLeft: 2, paddingRight: 2, marginRight: 1 ) )
                .AddRight( ScreenType.Default.Text( "0123456789" ).Box( paddingLeft: 2, paddingRight: 2 ) );
        h.Width.ShouldBe( h.NominalWidth ).ShouldBe( 30 + 10 + 4 );
        TextBlock.MinimalWidth.ShouldBe( 10 );
        h.MinWidth.ShouldBe( 30 + 2 * 3 );
        {
            DebugRenderer.Render( h ).ShouldBe( """
                  0123456789     0123456789     0123456789  ⮐

                """ );
        }
        {
            var s = h.SetWidth( 43 );
            DebugRenderer.Render( s ).ShouldBe( """
                  0123456789     0123456789    0123456789  ⮐

                """ );
        }
        {
            var s = h.SetWidth( 42 );
            DebugRenderer.Render( s ).ShouldBe( """
                  0123456789    0123456789    0123456789  ⮐

                """ );
        }
        {
            var s = h.SetWidth( 41 );
            DebugRenderer.Render( s ).ShouldBe( """
                  0123456789    0123456789   0123456789  ⮐

                """ );
        }
        {
            var s = h.SetWidth( 40 );
            DebugRenderer.Render( s ).ShouldBe( """
                  0123456789    0123456789   0123456789 ⮐

                """ );
        }
        {
            var s = h.SetWidth( 39 );
            DebugRenderer.Render( s ).ShouldBe( """
                 0123456789   0123456789   0123456789  ⮐

                """ );
        }
        {
            var s = h.SetWidth( 38 );
            DebugRenderer.Render( s ).ShouldBe( """
                 0123456789   0123456789   0123456789 ⮐

                """ );
        }
        {
            var s = h.SetWidth( 37 );
            DebugRenderer.Render( s ).ShouldBe( """
                 0123456789   0123456789  0123456789 ⮐

                """ );
        }
        {
            var s = h.SetWidth( 36 );
            DebugRenderer.Render( s ).ShouldBe( """
                 0123456789  0123456789  0123456789 ⮐

                """ );
            s.SetWidth( 35 ).ShouldBeSameAs( s );
            s.SetWidth( 0 ).ShouldBeSameAs( s );
        }
    }

    [Test]
    public void TableLayout_1_2()
    {
        var r = TableLayout.Create( ScreenType.Default.Text( "0" ).AddRight( ScreenType.Default.Text( "1" ) ) );
        var t = r.ShouldBeAssignableTo<TableLayout>();
        t.Width.ShouldBe( 2 );
        t.NominalWidth.ShouldBe( 2 );
        t.MinWidth.ShouldBe( 2 );
        var c = t.Rows.ShouldBeAssignableTo<HorizontalContent>();
        c.Cells.Length.ShouldBe( 2 );
        c.Cells[0].ShouldBeAssignableTo<ContentBox>();
        c.Cells[1].ShouldBeAssignableTo<ContentBox>();
        DebugRenderer.Render( r ).ShouldBe( """
                    01⮐

                    """ );
    }

    [Test]
    public void TableLayout_Collapsable_1_2()
    {
        var r = TableLayout.Create( new Collapsable( ScreenType.Default.Text( "0" ).AddRight( ScreenType.Default.Text( "1" ) ) ),
                                    new ColumnDefinition( minWidth: 5 ) );
        DebugRenderer.Render( r ).ShouldBe( """
                    > 0  1⮐

                    """ );
    }

    [Test]
    public void TableLayout_Collapsable_12_2()
    {
        static TextBlock T( string t ) => ScreenType.Default.Text( t );

        var r = TableLayout.Create( new Collapsable( T( "0" ).AddRight( T( "D" ) ) )
                                    .AddBelow( new Collapsable( T( "01" ).AddRight( T( "D" ) ) ) )
                                    .AddBelow( new Collapsable( T( "012" ).AddRight( T( "D" ) ) ) )
                                    .AddBelow( new Collapsable( T( "0123" ).AddRight( T( "D" ) ) ) )
                                    .AddBelow( new Collapsable( T( "01234" ).AddRight( T( "D" ) ) ) )
                                    .AddBelow( new Collapsable( T( "012345" ).AddRight( T( "D" ) ) ) )
                                    .AddBelow( new Collapsable( T( "0123456" ).AddRight( T( "D" ) ) ) )
                                    .AddBelow( new Collapsable( T( "01234567" ).AddRight( T( "D" ) ) ) )
                                    .AddBelow( new Collapsable( T( "012345678" ).AddRight( T( "D" ) ) ) )
                                    .AddBelow( new Collapsable( T( "0123456789" ).AddRight( T( "D" ) ) ) )
                                    .AddBelow( new Collapsable( T( "0123456789A" ).AddRight( T( "D" ) ) ) )
                                    .AddBelow( new Collapsable( T( "0123456789AB" ).AddRight( T( "D" ) ) ) )
                                    .AddBelow( new Collapsable( T( "0123456789ABC" ).AddRight( T( "D" ) ) ) ) );
        DebugRenderer.Render( r ).ShouldBe( """
                    > 0            D⮐
                    > 01           D⮐
                    > 012          D⮐
                    > 0123         D⮐
                    > 01234        D⮐
                    > 012345       D⮐
                    > 0123456      D⮐
                    > 01234567     D⮐
                    > 012345678    D⮐
                    > 0123456789   D⮐
                    > 0123456789A  D⮐
                    > 0123456789AB D⮐
                    > 0123456789ABCD⮐

                    """ );
    }

    [Test]
    public void empty_columns_handling()
    {
        var s = ScreenType.Default;

        IRenderable Line( string t ) => s.Unit.AddRight( s.Text( t + "1" ), s.EmptyString, s.Text( t + "2" ), s.EmptyString, s.Text( t + "3" ) );

        var lines = s.Unit.AddBelow( Line( "A" ), Line( "AAAAA" ) );
        var table = lines.TableLayout();
        var r = DebugRenderer.Render( table );
        r.ShouldBe( """
            A1    A2    A3    ⮐
            AAAAA1AAAAA2AAAAA3⮐

            """ );
    }


    [Test]
    public void Repo_like()
    {
        var all = ScreenType.Default.Unit.AddBelow( CreateRepo( "Some-Repo", "master", true ),
                                                    CreateRepo( "Some-other-Repo", "develop", false ) )
                                         .TableLayout();
        all.Width.ShouldBe( 18 + 8 + 39 );
        DebugRenderer.Render( all ).ShouldBe( """
                    * Some-Repo       master  https://github.com/org/Some-Repo       ⮐
                      Some-other-Repo develop https://github.com/org/Some-other-Repo ⮐

                    """ );
        all = all.SetWidth( all.Width - 1 );
        DebugRenderer.Render( all ).ShouldBe( """
                    * Some-Repo       master  https://github.com/org/Some-Repo      ⮐
                      Some-other-Repo develop https://github.com/org/Some-other     ⮐
                                              -Repo                                 ⮐

                    """ );
        all = all.SetWidth( all.Width - 2 );
        DebugRenderer.Render( all ).ShouldBe( """
                    * Some-Repo      master  https://github.com/org/Some-Repo     ⮐
                      Some-other     develop https://github.com/org/Some-other    ⮐
                      -Repo                  -Repo                                ⮐

                    """ );

        all = all.SetWidth( all.Width - 3 );
        DebugRenderer.Render( all ).ShouldBe( """
                    * Some-Repo     master  https://github.com/org/Some-Repo   ⮐
                      Some-other    develop https://github.com/org/Some-other  ⮐
                      -Repo                 -Repo                              ⮐

                    """ );

        all = all.SetWidth( all.Width - 2 );
        DebugRenderer.Render( all ).ShouldBe( """
                    * Some-Repo     master  https://github.com/org/Some-Repo ⮐
                      Some-other    develop https://github.com/org/Some      ⮐
                      -Repo                 -other-Repo                      ⮐

                    """ );

        static IRenderable CreateRepo( string name, string branchName, bool isDirty )
        {
            IRenderable folder = ScreenType.Default.Text( name ).HyperLink( new Uri( $"file:///{name}" ) );
            folder = isDirty
                        ? folder.Box( paddingRight: 1 ).AddLeft( ScreenType.Default.Text( "*" ).Box( paddingRight: 1 ) )
                        : folder.Box( paddingLeft: 2, paddingRight: 1 );
            folder = folder.Box();

            folder = folder.AddRight( ScreenType.Default.Text( branchName ).Box( marginRight: 1 ) );
            var url = $"https://github.com/org/{name}";
            folder = folder.AddRight( ScreenType.Default.Text( url ).HyperLink( new Uri( url ) ).Box( marginRight: 1 ) );
            return folder;
        }

    }

    sealed class ZCommand : Command
    {
        public ZCommand()
            : base( null,
                    "ze command",
                    """


                        Only here to
                    test display.
                    (text is trimmed.)   




                    """,
                    arguments: [("a1", """
                        Argument n°1 is
                        required like
                        all arguments. 
                        """)],
                    options: [
                        (["--options", "-o"], "This description should be prefixed with [Multiple].", Multiple: true),
                        (["--single", "-s"], "This description \"single\" is not multiple.", Multiple: false),
                        (["--others", "-o2"], """
                        Also multiple and
                        on multiple
                        lines.
                        """, Multiple: true),
                        ],
                    flags: [
                        (["--flag1", "-f1"], "Flag n°1."),
                        (["--flag2", "-f2"], "Flag n°2.")
                        ] )
        {
        }

        internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
        {
            return ValueTask.FromResult( true );
        }
    }

    [Test]
    public void basic_Console_help_display_for_plugin_section()
    {
        var commands = CKliCommands.Commands.GetForHelp( ScreenType.Default, "plugin", null );
        commands.Insert( 0, new CommandHelp( ScreenType.Default, new ZCommand() ) );

        var help = ScreenExtensions.CreateDisplayHelp( ScreenType.Default,
                                                       isInteractiveScreen: false,
                                                       commands,
                                                       CommandLineArguments.Empty, default, default );
        help.MinWidth.ShouldBe( 39 );
        help = help.SetWidth( 40 );

        string result = help.RenderAsString();
        Console.Write( result );
        result.ShouldContain( """
            > ze command <a1>            Only here  
            │                            to         
            │                            test       
            │                            display.   
            │                            (text is   
            │                            trimmed.)  
            │   <a1>                     Argument   
            │                            n°1 is     
            │                            required   
            │                            like       
            │                            all        
            │                            arguments.   
            │   Options:                           
            │    --options, -o           [Multiple] 
            │                            This       
            │                            description
            │                            should be  
            │                            prefixed   
            │                            with       
            │                            [Multiple].
            │    --single, -s            This       
            │                            description
            │                            "single" is
            │                            not        
            │                            multiple.  
            │    --others, -o2           [Multiple] 
            │                            Also       
            │                            multiple   
            │                            and        
            │                            on multiple
            │                            lines.     
            │   Flags:                             
            │    --flag1, -f1            Flag n°1.  
            │    --flag2, -f2            Flag n°2.
            """ );
    }


}
