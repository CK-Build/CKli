using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Core;

static class ScreenHelpers
{
    /// <summary>
    /// Creates renderable for a list of command helps, optionally followed by global options and flags.
    /// </summary>
    /// <param name="commands">The command helps to display.</param>
    /// <param name="cmdLine">The current command line.</param>
    /// <param name="globalOptions">The global options.</param>
    /// <param name="globalFlags">The global flags.</param>
    internal static IRenderable CreateDisplayHelp( List<CommandHelp> commands,
                                                       CommandLineArguments cmdLine,
                                                       ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> globalOptions,
                                                       ImmutableArray<(ImmutableArray<string> Names, string Description)> globalFlags,
                                                       int maxWidth )
    {
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
        // 
        // |---|--|                    |--|
        const int offsetArgTitle = 3;
        const int offsetArg = offsetArgTitle + 2;
        const int descriptionPadding = 3;

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

        int descriptionOffset = Math.Max( maxCommandPathWithArgsWitdh, maxArgOptFlagsWidth + offsetArg ) + descriptionPadding;
        // If the screen is too narrow, give up any wrapping.
        int descriptionMaxLength = maxWidth - descriptionOffset;
        if( descriptionMaxLength < TextBlock.MinWidth ) descriptionMaxLength = 0;

        var help = IRenderable.None.AddBelow(
            commands.Select( c =>
                c.CommandPathAndArgs.Box( right: descriptionOffset - c.CommandPathAndArgs.Width ).AddRight( c.Description.SetWidth( descriptionMaxLength ) )
                    .AddBelow( c.Arguments.Select( a => a.Name.Box( left: offsetArgTitle, right: descriptionOffset - a.Name.Width - offsetArgTitle )
                                                              .AddRight( a.Description.SetWidth( descriptionMaxLength ) ) ) )
                    .AddBelow( c.Options.Length > 0,
                               TextBlock.FromText( "Options:" ).Box( left: offsetArgTitle )
                                    .AddBelow( c.Options.Select( o => o.Names.Box( left: offsetArg, right: descriptionOffset - o.Names.Width - offsetArg )
                                                                             .AddRight( o.Description.SetWidth( descriptionMaxLength ) ) ) ) )
                    .AddBelow( c.Flags.Length > 0,
                               TextBlock.FromText( "Flags:" ).Box( left: offsetArgTitle )
                                    .AddBelow( c.Flags.Select( f => f.Names.Box( left: offsetArg, right: descriptionOffset - f.Names.Width - offsetArg )
                                                                             .AddRight( f.Description.SetWidth( descriptionMaxLength ) ) ) ) )
                    .AddBelow( TextBlock.EmptyString ) ),
               globalOptions.IsDefaultOrEmpty
                ? null
                : TextBlock.FromText( "Global options:" )
                      .AddBelow( CommandHelp.ToRenderableOptions( globalOptions )
                                    .Select( o => o.Names.Box( left: offsetArg, right: descriptionOffset - o.Names.Width - offsetArg )
                                                                .AddRight( o.Description.SetWidth( descriptionMaxLength ) ) ),
                                  TextBlock.EmptyString
                                ),
                globalFlags.IsDefaultOrEmpty
                ? null
                : TextBlock.FromText( "Global flags:" )
                      .AddBelow( CommandHelp.ToRenderableFlags( globalFlags )
                                    .Select( f => f.Names.Box( left: offsetArg, right: descriptionOffset - f.Names.Width - offsetArg )
                                                                             .AddRight( f.Description.SetWidth( descriptionMaxLength ) ) ) )

            );
        return help;
    }

    internal static IRenderable CreateDisplayPlugin( string headerText, List<World.DisplayInfoPlugin>? infos, int maxWidth )
    {
        IRenderable display = TextBlock.FromText( headerText );
        if( infos != null )
        {
            // Layout
            // ========================================
            // ShortName       | <Xml>                |
            //    TextStatus   |                      |
            //                 |                      |
            //    Message:
            //      <Message>
            // ========================================
            display = display.AddBelow(
                TextBlock.EmptyString,
                infos.Select(
                    i => new ContentBox( TextBlock.FromText( i.ShortName )
                                            .AddBelow( TextBlock.FromText( i.Status.GetTextStatus() ).Box( left: 4 ) ), right: 3 )
                            .AddRight( TextBlock.FromText( i.Configuration?.ToString() ) )
                            .AddBelow( i.Message != null
                                        ? TextBlock.FromText( "Message:" ).AddBelow( i.Message.Box( right: 3 ) ).Box( left: 2 )
                                        : null ) )
                );
        }
        return display;
    }
}

