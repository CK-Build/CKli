using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CKli.Core;

/// <summary>
/// Extends <see cref="IScreen"/>.
/// </summary>
public static class ScreenExtensions
{
    /// <summary>
    /// Helper that provides the <see cref="ScreenType"/> to build a renderable and displays it.
    /// </summary>
    /// <param name="screen">This screen.</param>
    /// <param name="renderable">The renderable factory.</param>
    /// <param name="newLine">False to not append a new line after the renderable.</param>
    public static void Display( this IScreen screen, Func<ScreenType, IRenderable> renderable, bool newLine = true )
    {
        screen.Display( renderable( screen.ScreenType ), newLine );
    }

    /// <summary>
    /// Displays the command helps on the screen. See <see cref="CreateDisplayHelpHeader"/> and <see cref="CreateDisplayHelp"/>.
    /// </summary>
    /// <param name="screen"></param>
    /// <param name="commands"></param>
    /// <param name="cmdLine"></param>
    /// <param name="globalOptions"></param>
    /// <param name="globalFlags"></param>
    public static void DisplayHelp( this IScreen screen,
                                    List<CommandHelp> commands,
                                    CommandLineArguments cmdLine,
                                    ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> globalOptions = default,
                                    ImmutableArray<(ImmutableArray<string> Names, string Description)> globalFlags = default )
    {
        var h = CreateDisplayHelp( screen.ScreenType,
                                   screen is InteractiveScreen,
                                   commands,
                                   cmdLine,
                                   globalOptions,
                                   globalFlags );
        screen.Display( h );
    }

    /// <summary>
    /// Renders the <see cref="CommandLineArguments.GetRemainingArguments()"/> only if it is not empty and 
    /// <see cref="CommandLineArguments.HasHelp"/> is false.
    /// <para>
    /// Public for tests.
    /// </para>
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="cmdLine">The command line that should have remaining arguments.</param>
    /// <returns>The renderable. May be <see cref="ScreenType.Unit"/>.</returns>
    public static IRenderable CreateDisplayHelpHeader( ScreenType screenType, CommandLineArguments cmdLine )
    {
        IRenderable header = screenType.Unit;
        if( !cmdLine.HasHelp && cmdLine.RemainingCount > 0 )
        {
            var args = cmdLine.GetRemainingArguments();
            header = screenType.Text( "Arguments:" ).Box()
                        .AddRight( args.Select(
                                    r => screenType.Text( r.Remaining
                                                            ? (r.Argument.Length > 1
                                                                ? $"{r.Argument}{Environment.NewLine}└{new string( '─', r.Argument.Length-2)}┘"
                                                                : $"{r.Argument}{Environment.NewLine}^"
                                                              )
                                                            : r.Argument,
                                                          r.Remaining
                                                            ? new TextStyle( TextEffect.Invert )
                                                            : new TextStyle( ConsoleColor.DarkGreen, ConsoleColor.Black ) )
                                                    .Box( marginLeft: 1, foreColor: ConsoleColor.DarkRed ) ) )
                        .AddBelow( screenType.EmptyString );
        }

        return header;
    }

    /// <summary>
    /// Creates renderable for a list of command helps, optionally followed by global options and flags.
    /// </summary>
    /// <remarks>
    /// Public for tests.
    /// </remarks>
    /// <param name="screenType">The screen type.</param>
    /// <param name="isInteractiveScreen">
    /// True if the screen is in interactive mode.
    /// In interactive mode, help is displayed only if explicitly required (the header is always displayed).
    /// </param>
    /// <param name="commands">The command helps to display.</param>
    /// <param name="cmdLine">The current command line.</param>
    /// <param name="globalOptions">The global options.</param>
    /// <param name="globalFlags">The global flags.</param>
    public static IRenderable CreateDisplayHelp( ScreenType screenType,
                                                 bool isInteractiveScreen,
                                                 List<CommandHelp> commands,
                                                 CommandLineArguments cmdLine,
                                                 ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> globalOptions,
                                                 ImmutableArray<(ImmutableArray<string> Names, string Description)> globalFlags )
    {
        IRenderable head = commands.Count == 1
                                ? CreateDisplayHelpHeader( screenType, cmdLine )
                                : screenType.Unit;
        // In interactive mode, help is displayed only if explicitly required.
        isInteractiveScreen |= cmdLine.HasInteractiveArgument;
        if( isInteractiveScreen && !cmdLine.HasHelp )
        {
            return head;
        }

        // Layout:
        // > command path <name1> <name2>   Description
        // |      <name1>                   Description
        // |      <name2>                   Description
        // |     Options:                   
        // |        --opt, -o               [Multiple] Description on
        // |                                more than one line.
        // |        --opt2                  Description
        // |     Flags:                     
        // |        --flag, -f              Description
        // 
        int minFirstCol = 0;
        var helps = screenType.Unit.AddBelow( commands.Select( c => new Collapsable( RenderCommand( c, ref minFirstCol )
                                                                                     .AddBelow( screenType.EmptyString ) )  ) );

        if( !globalOptions.IsDefaultOrEmpty )
        {
            helps = helps.AddBelow( screenType.Text( "Global options:" ) )
                         .AddBelow( CommandHelp.ToRenderableOptions( screenType, globalOptions )
                                        .Select( o => o.Names.Box( marginLeft: 1 ).AddRight( o.Description ) ) );
         }
        if( !globalFlags.IsDefaultOrEmpty )
        {
            helps = helps.AddBelow( screenType.Text( "Global flags:" ) )
                         .AddBelow( CommandHelp.ToRenderableFlags( screenType, globalFlags )
                                        .Select( f => f.Names.Box( marginLeft: 1 ).AddRight( f.Description ) ) );

        }
        helps = TableLayout.Create( helps, new ColumnDefinition( minWidth: 2 + minFirstCol ) );
        return head.AddBelow( helps );

        static IRenderable RenderCommand( CommandHelp c, ref int minFirstCol )
        {
            IRenderable head = c.CommandPathAndArgs.Box( marginRight: 1 );
            if( head.Width > minFirstCol ) minFirstCol = head.Width;

            head = head.AddRight( c.Description )
                       .AddBelow( c.Arguments.Select( a => a.Name.Box( marginLeft: 2, marginRight: 1 ).AddRight( a.Description ) ) );

            if( c.Options.Length > 0 )
            {
                head = head.AddBelow( c.ScreenType.Text( "Options:" ).Box( marginLeft: 2 ) )
                           .AddBelow( c.Options.Select( o => o.Names.Box( marginLeft: 3, marginRight: 1 ).AddRight( o.Description ) ) );
            }
            if( c.Flags.Length > 0 )
            {
                head = head.AddBelow( c.ScreenType.Text( "Flags:" ).Box( marginLeft: 2 ) )
                           .AddBelow( c.Flags.Select( f => f.Names.Box( marginLeft: 3, marginRight: 1 ).AddRight( f.Description ) ) );
            }
            return head;
        }

    }


    internal static void DisplayPluginInfo( this IScreen screen, string headerText, List<World.DisplayInfoPlugin>? infos )
    {
        var s = screen.ScreenType;
        IRenderable display = s.Text( headerText );
        if( infos != null )
        {
            // Layout:
            // > ShortName       | <Xml>                
            // |   TextStatus    |                      
            // |   Version       |                      
            // | Message:
            // |    <Message>

            display = display.AddBelow(
                s.EmptyString,
                infos.Select(
                    i => new Collapsable(
                            new ContentBox( s.Text( i.ShortName )
                                            .AddBelow(
                                                s.Text( i.Status.GetTextStatus() )
                                                .AddBelow( s.Text( i.Version?.Version?.ToString() ?? "<source based>", TextEffect.Italic )
                                                ).Box( paddingLeft: 3 ) ), paddingRight: 3 )
                            .AddRight( s.Text( i.Configuration?.ToString() ).Collapsable() )
                            .AddBelow( i.Message != null
                                        ? s.Text( "Message:", ConsoleColor.DarkYellow )
                                           .AddBelow( i.Message.Box( paddingLeft: 3, foreColor: ConsoleColor.Yellow ) )
                                        : null ) ) )
                );
        }
        screen.Display( display );
    }
}

