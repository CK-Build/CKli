using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Public for tests.
/// </summary>
public static class ScreenHelpers
{
    /// <summary>
    /// Renders the <see cref="CommandLineArguments.GetRemainingArguments()"/> only if it is not empty and 
    /// <see cref="CommandLineArguments.HasHelp"/> is false.
    /// <para>
    /// Public for tests.
    /// </para>
    /// </summary>
    /// <param name="cmdLine">The command line that should have renamint arguments.</param>
    /// <returns>The renderable. May be <see cref="IRenderable.Unit"/>.</returns>
    public static IRenderable CreateDisplayHelpHeader( CommandLineArguments cmdLine )
    {
        IRenderable header = IRenderable.Unit;
        if( !cmdLine.HasHelp && cmdLine.RemainingCount > 0 )
        {
            var args = cmdLine.GetRemainingArguments();
            header = TextBlock.FromText( "Arguments:" ).Box()
                        .AddRight( args.Select( r => TextBlock.FromText( r.Remaining
                                                                            ? $"{r.Argument}{Environment.NewLine}└{new string( '─', r.Argument.Length-2)}┘"
                                                                            : r.Argument,
                                                                         r.Remaining
                                                                             ? new TextStyle( TextEffect.Invert )
                                                                             : default ).Box( marginLeft: 1 ) ) )
                        .AddBelow( TextBlock.EmptyString );
        }

        return header;
    }

    /// <summary>
    /// Creates renderable for a list of command helps, optionally followed by global options and flags.
    /// </summary>
    /// <remarks>
    /// Public for tests.
    /// </remarks>
    /// <param name="commands">The command helps to display.</param>
    /// <param name="cmdLine">The current command line.</param>
    /// <param name="globalOptions">The global options.</param>
    /// <param name="globalFlags">The global flags.</param>
    public static IRenderable CreateDisplayHelp( List<CommandHelp> commands,
                                                 CommandLineArguments cmdLine,
                                                 ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> globalOptions,
                                                 ImmutableArray<(ImmutableArray<string> Names, string Description)> globalFlags,
                                                 int maxWidth )
    {
        IRenderable header = commands.Count == 1 ? CreateDisplayHelpHeader( cmdLine ) : IRenderable.Unit;

        // Layout:
        // > command path <name1> <name2>   Description
        // |     <name1>                    Description
        // |     <name2>                    Description
        // |     Options:                   
        // |        --opt, -o               [Multiple] Description on
        // |                                more than one line.
        // |        --opt2                  Description
        // |     Flags:                     
        // |        --flag, -f              Description
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

        var help = header.AddBelow(
            commands.Select( c => new Collapsable( RenderCommand( c, offsetArgTitle, offsetArg, descriptionOffset, descriptionMaxLength ) )
                                        .AddBelow( TextBlock.EmptyString ) ),
               globalOptions.IsDefaultOrEmpty
                ? null
                : TextBlock.FromText( "Global options:" )
                      .AddBelow( CommandHelp.ToRenderableOptions( globalOptions )
                                    .Select( o => o.Names.Box( paddingLeft: offsetArg, paddingRight: descriptionOffset - o.Names.Width - offsetArg )
                                                                .AddRight( o.Description.SetTextWidth( descriptionMaxLength ) ) ),
                                  TextBlock.EmptyString
                                ),
                globalFlags.IsDefaultOrEmpty
                ? null
                : TextBlock.FromText( "Global flags:" )
                      .AddBelow( CommandHelp.ToRenderableFlags( globalFlags )
                                    .Select( f => f.Names.Box( paddingLeft: offsetArg, paddingRight: descriptionOffset - f.Names.Width - offsetArg )
                                                                             .AddRight( f.Description.SetTextWidth( descriptionMaxLength ) ) ) )

            );
        return help;
    }

    private static IRenderable RenderCommand( CommandHelp c, int offsetArgTitle, int offsetArg, int descriptionOffset, int descriptionMaxLength )
    {
        return c.CommandPathAndArgs.Box( paddingRight: descriptionOffset - c.CommandPathAndArgs.Width ).AddRight( c.Description.SetTextWidth( descriptionMaxLength ) )
                            .AddBelow( c.Arguments.Select( a => a.Name.Box( paddingLeft: offsetArgTitle, paddingRight: descriptionOffset - a.Name.Width - offsetArgTitle )
                                                                      .AddRight( a.Description.SetTextWidth( descriptionMaxLength ) ) ) )
                            .AddBelow( c.Options.Length > 0,
                                       TextBlock.FromText( "Options:" ).Box( paddingLeft: offsetArgTitle )
                                            .AddBelow( c.Options.Select( o => o.Names.Box( paddingLeft: offsetArg, paddingRight: descriptionOffset - o.Names.Width - offsetArg )
                                                                                     .AddRight( o.Description.SetTextWidth( descriptionMaxLength ) ) ) ) )
                            .AddBelow( c.Flags.Length > 0,
                                       TextBlock.FromText( "Flags:" ).Box( paddingLeft: offsetArgTitle )
                                            .AddBelow( c.Flags.Select( f => f.Names.Box( paddingLeft: offsetArg, paddingRight: descriptionOffset - f.Names.Width - offsetArg )
                                                                                     .AddRight( f.Description.SetTextWidth( descriptionMaxLength ) ) ) ) );
    }

    internal static IRenderable CreateDisplayPlugin( string headerText, List<World.DisplayInfoPlugin>? infos, int maxWidth )
    {
        IRenderable display = TextBlock.FromText( headerText );
        if( infos != null )
        {
            // Layout:
            // > ShortName       | <Xml>                
            // |   TextStatus    |                      
            // |                 |                      
            // | Message:
            // |    <Message>

            display = display.AddBelow(
                TextBlock.EmptyString,
                infos.Select(
                    i => new Collapsable(
                            new ContentBox( TextBlock.FromText( i.ShortName )
                                            .AddBelow( TextBlock.FromText( i.Status.GetTextStatus() ).Box( paddingLeft: 3 ) ), paddingRight: 3 )
                            .AddRight( TextBlock.FromText( i.Configuration?.ToString() ) )
                            .AddBelow( i.Message != null
                                        ? TextBlock.FromText( "Message:" ).AddBelow( i.Message.Box( paddingLeft: 3 ) )
                                        : null ) ) )
                );
        }
        return display;
    }
}

