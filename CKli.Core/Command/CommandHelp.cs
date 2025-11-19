using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace CKli.Core;

/// <summary>
/// Intermediate projection of a <see cref="Command"/> to ease building the rendered help.
/// </summary>
public sealed class CommandHelp
{
    readonly ScreenType _screenType;
    readonly Command _command;
    readonly TextBlock _commandPathAndArgs;
    readonly TextBlock _description;
    readonly ImmutableArray<(TextBlock Name, TextBlock Description)> _arguments;
    readonly ImmutableArray<(TextBlock Names, TextBlock Description)> _options;
    readonly ImmutableArray<(TextBlock Names, TextBlock Description)> _flags;

    /// <summary>
    /// Initializes a new <see cref="CommandHelp"/>.
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="c">The command.</param>
    public CommandHelp( ScreenType screenType, Command c )
    {
        _screenType = screenType;
        _command = c;
        _description = screenType.Text( c.Description );
        var styleCommand = new TextStyle( System.ConsoleColor.DarkGreen, System.ConsoleColor.Black, TextEffect.Italic );
        // Arguments.
        var args = new (TextBlock, TextBlock)[c.Arguments.Length];
        for( int i = 0; i < args.Length; i++ )
        {
            var a = c.Arguments[i];
            args[i] = (screenType.Text( $"<{a.Name}>", styleCommand ), screenType.Text( a.Description ));
        }
        _arguments = ImmutableCollectionsMarshal.AsImmutableArray( args );
        _commandPathAndArgs = screenType.Text( $"{c.CommandPath} {string.Join( ' ', _arguments.Select( a => a.Name.RawText ) )}", styleCommand );
        // Options.
        _options = ToRenderableOptions( screenType, c.Options );
        // Flags.
        _flags = ToRenderableFlags( screenType, c.Flags );
    }

    /// <summary>
    /// Gets the screen type.
    /// </summary>
    public ScreenType ScreenType => _screenType;

    /// <summary>
    /// Gets the command.
    /// </summary>
    public Command Command => _command;

    /// <summary>
    /// Gets the command path and its arguments.
    /// </summary>
    public TextBlock CommandPathAndArgs => _commandPathAndArgs;

    /// <summary>
    /// Gets the command description.
    /// </summary>
    public TextBlock Description => _description;

    /// <summary>
    /// Gets the arguments and their description.
    /// </summary>
    public ImmutableArray<(TextBlock Name, TextBlock Description)> Arguments => _arguments;

    /// <summary>
    /// Gets the options names and their description.
    /// </summary>
    public ImmutableArray<(TextBlock Names, TextBlock Description)> Options => _options;

    /// <summary>
    /// Gets the flag names and their description.
    /// </summary>
    public ImmutableArray<(TextBlock Names, TextBlock Description)> Flags => _flags;

    /// <summary>
    /// Reusable helper to build <see cref="Flags"/>.
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="fDesc">The command's flags and description.</param>
    /// <returns>Renderable flags.</returns>
    public static ImmutableArray<(TextBlock Names, TextBlock Description)> ToRenderableFlags( ScreenType screenType,
                                                                                              ImmutableArray<(ImmutableArray<string> Names, string Description)> fDesc )
    {
        var flags = new (TextBlock, TextBlock)[fDesc.Length];
        for( int i = 0; i < flags.Length; i++ )
        {
            var f = fDesc[i];
            flags[i] = (screenType.Text( string.Join( ", ", f.Names ) ), screenType.Text( f.Description ));
        }
        return ImmutableCollectionsMarshal.AsImmutableArray( flags );
    }

    /// <summary>
    /// Reusable helper to build <see cref="Options"/>.
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="oDesc">The command's options and description.</param>
    /// <returns>Renderable options.</returns>
    public static ImmutableArray<(TextBlock Names, TextBlock Description)> ToRenderableOptions( ScreenType screenType,
                                                                                               ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> oDesc )
    {
        var aOpts = new (TextBlock, TextBlock)[oDesc.Length];
        for( int i = 0; i < aOpts.Length; i++ )
        {
            var o = oDesc[i];
            aOpts[i] = (screenType.Text( string.Join( ", ", o.Names ) ), screenType.Text( o.Multiple ? "[Multiple] " + o.Description : o.Description ) );
        }
        return ImmutableCollectionsMarshal.AsImmutableArray( aOpts );
    }

}
