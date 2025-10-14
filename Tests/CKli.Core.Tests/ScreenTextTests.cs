using CK.Core;
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
    public void simple_line_test()
    {
        {
            var line = TextBlock.FromText( "Hello" ).AddRight( TextBlock.FromText( "world" ) );
            line.RenderAsString().ShouldBe( """
                Helloworld

                """ );
        }
        {
            var line = TextBlock.FromText( "Hello" ).Box( paddingLeft: 1, paddingRight: 1 ).AddRight( TextBlock.FromText( "world" ) );
            line.RenderAsString().ShouldBe( """
                 Hello world

                """ );
        }
        {
            var line = TextBlock.FromText( "Hello" ).Box( paddingLeft: 1, paddingRight: 1 )
                            .AddRight( TextBlock.FromText( "world" ).Box( paddingLeft: 2, paddingRight: 2 ) )
                            .AddRight( TextBlock.FromText( "!" ) );
            line.RenderAsString().ShouldBe( """
                 Hello   world  !

                """ );
        }
    }

    [Test]
    public void simple_2_lines_test()
    {
        {
            var line = TextBlock.FromText( "Message:" )
                        .AddBelow( TextBlock.FromText( "Hello world!" ).Box( marginLeft: 3 ) );
            line.RenderAsString().ShouldBe( """
                Message:
                   Hello world!

                """ );
        }
        {
            var line = TextBlock.FromText( "Message:" )
                        .AddBelow( TextBlock.FromText( "Hello world!" ).Box( paddingLeft: 3 ) );
            line.RenderAsString().ShouldBe( """
                Message:
                   Hello world!

                """ );
        }
        {
            var line = TextBlock.FromText( """


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
            var t = TextBlock.FromText( "0 1 2 3 4 5 6 7 8 9 A B C D E F G H I J K L M N O P Q R S T U V W X Y Z" );
            var tMin = t.SetTextWidth( TextBlock.MinWidth );
            tMin.Width.ShouldBe( TextBlock.MinWidth );
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
            var t = TextBlock.FromText( "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ" );
            var tMin = t.SetTextWidth( TextBlock.MinWidth );
            tMin.Width.ShouldBe( TextBlock.MinWidth );
            tMin.Height.ShouldBe( 3 );
            tMin.RenderAsString().ShouldBe( """
                0123456789ABCDE
                FGHIJKLMNOPQRST
                UVWXYZ

                """ );
        }
        { 
            var t = TextBlock.FromText( "La liberté des uns s'arrête où commence celle des autres." );
            var tMin = t.SetTextWidth( TextBlock.MinWidth );
            tMin.Width.ShouldBe( TextBlock.MinWidth );
            tMin.Height.ShouldBe( 4 );
            tMin.RenderAsString().ShouldBe( """
                La liberté des
                uns s'arrête où
                commence celle
                des autres.

                """ );
            var tMin1 = t.SetTextWidth( TextBlock.MinWidth + 1 );
            tMin1.RenderAsString().ShouldBe( tMin.RenderAsString() );
            var tMin2 = tMin1.SetTextWidth( TextBlock.MinWidth + 2 );
            tMin2.RenderAsString().ShouldBe( tMin.RenderAsString() );
            var tMin3 = tMin2.SetTextWidth( TextBlock.MinWidth + 3 );
            tMin3.Height.ShouldBe( 4 );
            tMin3.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où
                commence celle des
                autres.

                """ );
            var tMin10 = tMin2.SetTextWidth( TextBlock.MinWidth + 10 );
            tMin10.Height.ShouldBe( 3 );
            tMin10.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où commence
                celle des autres.

                """ );

            var tMin11 = tMin2.SetTextWidth( TextBlock.MinWidth + 11 );
            tMin11.Height.ShouldBe( 3 );
            tMin11.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où commence celle
                des autres.

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
        var header = ScreenHelpers.CreateDisplayHelpHeader( cmdLine );
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
        var commands = CKliCommands.Commands.GetForHelp( "plugin", null );
        commands.Add( new CommandHelp( new ZCommand() ) );

        var help = ScreenHelpers.CreateDisplayHelp( commands, new CommandLineArguments( [] ), default, default, IScreen.MaxScreenWidth );

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
