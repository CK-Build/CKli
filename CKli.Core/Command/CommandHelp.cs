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
    readonly CommandNamespaceItem _item;
    readonly TextBlock _commandPathAndArgs;
    readonly TextBlock _description;
    readonly ImmutableArray<(TextBlock Name, TextBlock Description)> _arguments;
    readonly ImmutableArray<(TextBlock Names, TextBlock Description)> _options;
    readonly ImmutableArray<(TextBlock Names, TextBlock Description)> _flags;
    readonly ImmutableArray<IRenderable> _helpLinks;

    /// <summary>
    /// Initializes a new <see cref="CommandHelp"/> for a command or for a pure namespace.
    /// A pure namespace has no <see cref="Arguments"/>, <see cref="Options"/> nor <see cref="Flags"/>.
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="item">The command or the pure namespace.</param>
    public CommandHelp( ScreenType screenType, CommandNamespaceItem item )
    {
        _screenType = screenType;
        _item = item;
        _description = screenType.Text( item.Description, style: TextStyle.Default );
        var styleCommand = new TextStyle( System.ConsoleColor.DarkGreen, effect: TextEffect.Italic );
        var c = item as Command;
        // Arguments.
        var args = new (TextBlock, TextBlock)[c?.Arguments.Length ?? 0];
        for( int i = 0; i < args.Length; i++ )
        {
            var a = c!.Arguments[i];
            args[i] = (screenType.Text( $"<{a.Name}>", styleCommand ), screenType.Text( a.Description ));
        }
        _arguments = ImmutableCollectionsMarshal.AsImmutableArray( args );
        _commandPathAndArgs = screenType.Text( $"{item.CommandPath} {string.Join( ' ', _arguments.Select( a => a.Name.RawText ) )}", styleCommand );
        // Options.
        _options = c != null ? ToRenderableOptions( screenType, c.Options ) : [];
        // Flags.
        _flags = c != null ? ToRenderableFlags( screenType, c.Flags ) : [];
        // Help links.
        _helpLinks = item.HelpUrls
                         .Select( u => (IRenderable)screenType.Text( u.ToString(), System.ConsoleColor.Blue ).HyperLink( u ) )
                         .ToImmutableArray();
    }

    /// <summary>
    /// Gets the screen type.
    /// </summary>
    public ScreenType ScreenType => _screenType;

    /// <summary>
    /// Gets the command or the pure namespace.
    /// </summary>
    public CommandNamespaceItem Item => _item;

    /// <summary>
    /// Gets the command. Null when <see cref="Item"/> is a pure namespace.
    /// </summary>
    public Command? Command => _item as Command;

    /// <summary>
    /// Gets the renderable links to the external documentation of <see cref="Item"/>.
    /// Empty when no <see cref="CommandNamespaceItem.HelpUrls"/> exist.
    /// </summary>
    public ImmutableArray<IRenderable> HelpLinks => _helpLinks;

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
