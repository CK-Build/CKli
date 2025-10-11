using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Core.Tests;

[TestFixture]
public class ScreenTextTests
{
    sealed class ZCommand : Command
    {
        public ZCommand()
            : base( null,
                    "ze command",
                    """
                    Only here to
                    test display (trimEnd is the default).




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
        var commands = CKliCommands.Commands.GetForHelp( "plugin", null ).Append( new CommandHelp( new ZCommand() ) );

        // Layout:
        //  command path <name1> <name2>   Description
        //      <name1>                    Description
        //      <name2>                    Description
        //      Options:                   
        //         --opt, -o               [Multiple] Description on
        //                                 more than one line.
        //         --opt2                  Description
        //      Flags:                     
        //         --flag, -f              Description
        // |---|--|                    |--|
        int maxCommandPathWithArgsWitdh = 0;
        int maxArgOptFlagsWidth = 0;
        foreach( var c in commands )
        {
            if( c.CommandPathAndArgs.Width > maxCommandPathWithArgsWitdh ) maxCommandPathWithArgsWitdh = c.CommandPathAndArgs.Width;
            int argOptFlagsWidth = c.Arguments.Select( o => o.Name.Width )
                                    .Concat( c.Options.Select( o => o.Names.Width ) )
                                    .Concat( c.Flags.Select( o => o.Names.Width ) )
                                    .DefaultIfEmpty()
                                    .Max();
            if( argOptFlagsWidth > maxArgOptFlagsWidth ) maxArgOptFlagsWidth = argOptFlagsWidth;
        }

        const int offsetArgTitle = 3;
        const int offsetArg = offsetArgTitle + 2;
        const int descriptionPadding = 3;

        int descriptionOffset = Math.Max( maxCommandPathWithArgsWitdh, maxArgOptFlagsWidth + offsetArg ) + descriptionPadding;

        var help = IRenderable.None.AddBelow(
            commands.Select( c =>
                c.CommandPathAndArgs.Box( right: descriptionOffset - c.CommandPathAndArgs.Width ).AddRight( c.Description )
                    .AddBelow( c.Arguments.Select( a => a.Name.Box( left: offsetArgTitle, right: descriptionOffset - a.Name.Width - offsetArgTitle )
                                                              .AddRight( a.Description ) ) )
                    .AddBelow( c.Options.Length > 0,
                               TextBlock.FromText( "Options:" ).Box( left: offsetArgTitle )
                                    .AddBelow( c.Options.Select( o => o.Names.Box( left: offsetArg, right: descriptionOffset - o.Names.Width - offsetArg )
                                                                             .AddRight( o.Description ) ) ) )
                    .AddBelow( c.Flags.Length > 0,
                               TextBlock.FromText( "Flags:" ).Box( left: offsetArgTitle )
                                    .AddBelow( c.Flags.Select( f => f.Names.Box( left: offsetArg, right: descriptionOffset - f.Names.Width - offsetArg )
                                                                             .AddRight( f.Description ) ) ) )
                    .AddBelow( TextBlock.EmptyString ) )
            );

        string result = help.RenderAsString();

        Console.Write( result );
        result.ShouldContain( """
            ze command <a1>              Only here to
                                         test display (trimEnd is the default).
               <a1>                      Argument n°1 is
                                         required like
                                         all arguments.
               Options:
                 --options, -o           [Multiple] This description should be prefixed with [Multiple].
                 --single, -s            This description one is not multiple.
                 --others, -o2           [Multiple] Also multiple and
                                         on multiple
                                         lines.
               Flags:
                 --flag1, -f1            Flag n°1.
                 --flag2, -f2            Flag n°2.

            """ );
    }

    [Test]
    public void TextBlock_witdh_adjustement()
    {
        {
            var t = TextBlock.FromText( "0 1 2 3 4 5 6 7 8 9 A B C D E F G H I J K L M N O P Q R S T U V W X Y Z" );
            var tMin = t.SetWidth( TextBlock.MinWidth );
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
            var tMin = t.SetWidth( TextBlock.MinWidth );
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
            var tMin = t.SetWidth( TextBlock.MinWidth );
            tMin.Width.ShouldBe( TextBlock.MinWidth );
            tMin.Height.ShouldBe( 4 );
            tMin.RenderAsString().ShouldBe( """
                La liberté des
                uns s'arrête où
                commence celle
                des autres.

                """ );
            var tMin1 = t.SetWidth( TextBlock.MinWidth + 1 );
            tMin1.RenderAsString().ShouldBe( tMin.RenderAsString() );
            var tMin2 = tMin1.SetWidth( TextBlock.MinWidth + 2 );
            tMin2.RenderAsString().ShouldBe( tMin.RenderAsString() );
            var tMin3 = tMin2.SetWidth( TextBlock.MinWidth + 3 );
            tMin3.Height.ShouldBe( 4 );
            tMin3.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où
                commence celle des
                autres.

                """ );
            var tMin10 = tMin2.SetWidth( TextBlock.MinWidth + 10 );
            tMin10.Height.ShouldBe( 3 );
            tMin10.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où commence
                celle des autres.

                """ );

            var tMin11 = tMin2.SetWidth( TextBlock.MinWidth + 11 );
            tMin11.Height.ShouldBe( 3 );
            tMin11.RenderAsString().ShouldBe( """
                La liberté des uns
                s'arrête où commence celle
                des autres.

                """ );
        }
    }
}
