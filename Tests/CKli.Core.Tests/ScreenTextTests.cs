using LibGit2Sharp;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Core.Tests;

[TestFixture]
public class ScreenTextTests
{
    [Test]
    public void basic_Console_help_display_for_plugin_section()
    {
        var commands = CKliCommands.Commands.GetForHelp( "plugin", null );

        // Layout:
        //  command path <name1> <name2>   Description
        //         <name1>                 Description
        //         <name2>                 Description
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
        const int descriptionPadding = 4;

        int descriptionOffset = Math.Max( maxCommandPathWithArgsWitdh, maxArgOptFlagsWidth + offsetArg ) + descriptionPadding;

        var help = ILineRenderable.None.AddBelow(
            commands.Select( c =>
                c.CommandPathAndArgs.Box( right: descriptionOffset - c.CommandPathAndArgs.Width ).AddLeft( c.Description )
                    .AddBelow( c.Arguments.Select( a => a.Name.Box( left: offsetArg, right: descriptionOffset - a.Name.Width - offsetArg ).AddLeft( a.Description ) ) )
                    .AddBelow( c.Options.Length > 0,
                               new WordBlock( "Options:" ).Box( left: offsetArgTitle )
                                    .AddBelow( c.Options.Select( o => o.Names.Box( left: offsetArg, right: descriptionOffset - o.Names.Width - offsetArg )
                                                                             .AddLeft( o.Description.AddRight( o.Multiple
                                                                                                                ? new WordBlock( "[Multiple] " )
                                                                                                                : null ) ) ) ) )
                    .AddBelow( c.Flags.Length > 0,
                               new WordBlock( "Flags:" ).Box( left: offsetArgTitle )
                                    .AddBelow( c.Flags.Select( f => f.Names.Box( left: offsetArg, right: descriptionOffset - f.Names.Width - offsetArg )
                                                                             .AddLeft( f.Description ) ) ) )
                    .AddBelow( ILineRenderable.EmptyString ) )
            );

        var b = new StringBuilder();
        for( int i = 0; i < help.Height; i++ )
        {
            help.RenderLine( i, b, ( span, b ) => b.Append( span ) );
            b.AppendLine();
        }
        Console.Write( b.ToString() );

    }
}
