using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.Threading.Tasks;
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
    public void ContentAlign_Horizontal_test()
    {
        var text = StringScreenType.Default.Text( """
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
        var text = StringScreenType.Default.Text( """
                Hello world,

                I'm glad to be there...
                But...
                I'm sad that Trump's here.
                """ );
        var bigCell = StringScreenType.Default.Text( """
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
            //var lineUnk = text.Box().AddRight( bigCell );
            //var lineTop = text.Box( ContentAlign.VTop ).AddRight( bigCell );
            //string result = """
            //    Hello world,··············|1
            //    ··························|2
            //    I'm glad to be there...···|3
            //    But...····················|4
            //    I'm sad that Trump's here.|5
            //    ··························|6
            //    ··························|7
            //    ··························|8
            //    ··························|9
            //    ··························|10

            //    """.Replace( '·', ' ' );
            //lineUnk.RenderAsString().ShouldBe( result );
            //lineTop.RenderAsString().ShouldBe( result );
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
            var line = StringScreenType.Default.Text( "Hello" ).AddRight( StringScreenType.Default.Text( "world" ) );
            line.RenderAsString().ShouldBe( """
                Helloworld

                """ );
        }
        {
            var line = StringScreenType.Default.Text( "Hello" ).Box( paddingLeft: 1, paddingRight: 1 ).AddRight( StringScreenType.Default.Text( "world" ) );
            line.RenderAsString().ShouldBe( """
                 Hello world

                """ );
        }
        {
            var line = StringScreenType.Default.Text( "Hello" ).Box( paddingLeft: 1, paddingRight: 1 )
                            .AddRight( StringScreenType.Default.Text( "world" ).Box( paddingLeft: 2, paddingRight: 2 ) )
                            .AddRight( StringScreenType.Default.Text( "!" ) );
            line.RenderAsString().ShouldBe( """
                 Hello   world  !

                """ );
        }
    }

    [Test]
    public void simple_2_lines_test()
    {
        {
            var line = StringScreenType.Default.Text( "Message:" )
                        .AddBelow( StringScreenType.Default.Text( "Hello world!" ).Box( marginLeft: 3 ) );
            line.RenderAsString().ShouldBe( """
                Message:
                   Hello world!

                """ );
        }
        {
            var line = StringScreenType.Default.Text( "Message:" )
                        .AddBelow( StringScreenType.Default.Text( "Hello world!" ).Box( paddingLeft: 3 ) );
            line.RenderAsString().ShouldBe( """
                Message:
                   Hello world!

                """ );
        }
        {
            var line = StringScreenType.Default.Text( """


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
            var t = StringScreenType.Default.Text( "            0 1 2 3 4 5 6 7 8 9 A B C D E F G H I J K L M N O P Q R S T U V W X Y Z               " );
            var tMin = t.SetTextWidth( 15 );
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
            var t = StringScreenType.Default.Text( "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ" );
            var tMin = t.SetTextWidth( 15 );
            tMin.Width.ShouldBe( 15 );
            tMin.Height.ShouldBe( 3 );
            tMin.RenderAsString().ShouldBe( """
                0123456789ABCDE
                FGHIJKLMNOPQRST
                UVWXYZ

                """ );
        }
        { 
            var t = StringScreenType.Default.Text( "La liberté des uns s'arrête où commence celle des autres." );
            var tMin = t.SetTextWidth( 15 );
            tMin.Width.ShouldBe( 15 );
            tMin.Height.ShouldBe( 4 );
            tMin.RenderAsString().ShouldBe( """
                La liberté des
                uns s'arrête où
                commence celle
                des autres.

                """ );
            var tMin1 = t.SetTextWidth( 15 + 1 );
            tMin1.RenderAsString().ShouldBe( tMin.RenderAsString() );
            var tMin2 = tMin1.SetTextWidth( 15 + 2 );
            tMin2.RenderAsString().ShouldBe( tMin.RenderAsString() );
            var tMin3 = tMin2.SetTextWidth( 15 + 3 );
            tMin3.Height.ShouldBe( 4 );
            tMin3.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où
                commence celle des
                autres.

                """ );
            var tMin10 = tMin2.SetTextWidth( 15 + 10 );
            tMin10.Height.ShouldBe( 3 );
            tMin10.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où commence
                celle des autres.

                """ );

            var tMin11 = tMin2.SetTextWidth( 15 + 11 );
            tMin11.Height.ShouldBe( 3 );
            tMin11.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où commence celle
                des autres.

                """ );
        }
        {
            var t = StringScreenType.Default.Text( """
                                A
                                B            

                                C       


                """ );
            var tMin = t.SetTextWidth( 15 );
            tMin.ShouldBeSameAs( t );
            t.RenderAsString().ShouldBe( """
                A
                B

                C

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
        var header = ScreenHelpers.CreateDisplayHelpHeader( StringScreenType.Default, cmdLine );
        string result = header.RenderAsString();

        Console.Write( result );
        result.ShouldBe( """
                    Arguments: some a -fX -f1 -o1 O1 alien -om OM1 OM2 -os OS --fY -o1 O1TOOmuch alien -om OM3 -unk
                                      └─┘            └───┘                    └──┘ └─┘ └───────┘ └───┘         └──┘

                    
                    """ );

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
                        (["--single", "-s"], "This description one is not multiple.", Multiple: false),
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

        protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
        {
            return ValueTask.FromResult( true );
        }
    }

    [Test]
    public void basic_Console_help_display_for_plugin_section()
    {
        var commands = CKliCommands.Commands.GetForHelp( StringScreenType.Default, "plugin", null );
        commands.Add( new CommandHelp( StringScreenType.Default, new ZCommand() ) );

        var help = ScreenHelpers.CreateDisplayHelp( StringScreenType.Default,
                                                    commands,
                                                    new CommandLineArguments( [] ), default, default, IScreen.MaxScreenWidth );

        string result = help.RenderAsString();

        Console.Write( result );
        result.ShouldContain( """
            > ze command <a1>              Only here to
            │                              test display.
            │                              (text is trimmed.)
            │    <a1>                      Argument n°1 is
            │                              required like
            │                              all arguments.
            │    Options:
            │      --options, -o           [Multiple] This description should be prefixed with [Multiple].
            │      --single, -s            This description one is not multiple.
            │      --others, -o2           [Multiple] Also multiple and
            │                              on multiple
            │                              lines.
            │    Flags:
            │      --flag1, -f1            Flag n°1.
            │      --flag2, -f2            Flag n°2.

            """ );
    }

}
