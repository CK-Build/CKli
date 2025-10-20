using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace CKli.Core;

public class CommandHelp
{
    readonly ScreenType _screenType;
    readonly Command _command;
    readonly TextBlock _commandPath;
    readonly IRenderable _commandPathAndArgs;
    readonly TextBlock _description;
    readonly ImmutableArray<(TextBlock Name, TextBlock Description)> _arguments;
    readonly ImmutableArray<(TextBlock Names, TextBlock Description)> _options;
    readonly ImmutableArray<(TextBlock Names, TextBlock Description)> _flags;

    public CommandHelp( ScreenType screenType, Command c )
    {
        _screenType = screenType;
        _command = c;
        _commandPath = screenType.Text( c.CommandPath );
        _description = screenType.Text( c.Description );
        // Arguments.
        var args = new (TextBlock, TextBlock)[c.Arguments.Length];
        for( int i = 0; i < args.Length; i++ )
        {
            var a = c.Arguments[i];
            args[i] = (screenType.Text( $"<{a.Name}>" ), screenType.Text( a.Description ));
        }
        _arguments = ImmutableCollectionsMarshal.AsImmutableArray( args );
        _commandPathAndArgs = _commandPath.AddRight( _arguments.Select( a => a.Name.Box( paddingLeft: 1 ) ) );
        // Options.
        _options = ToRenderableOptions( screenType, c.Options );
        // Flags.
        _flags = ToRenderableFlags( screenType, c.Flags );
    }

    public ScreenType ScreenType => _screenType;

    public Command Command => _command;

    public TextBlock CommandPath => _commandPath;

    public IRenderable CommandPathAndArgs => _commandPathAndArgs;

    public TextBlock Description => _description;

    public ImmutableArray<(TextBlock Name, TextBlock Description)> Arguments => _arguments;

    public ImmutableArray<(TextBlock Names, TextBlock Description)> Options => _options;

    public ImmutableArray<(TextBlock Names, TextBlock Description)> Flags => _flags;

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
