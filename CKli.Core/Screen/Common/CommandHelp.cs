using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace CKli.Core;

public class CommandHelp
{
    readonly Command _command;
    readonly TextBlock _commandPath;
    readonly IRenderable _commandPathAndArgs;
    readonly TextBlock _description;
    readonly ImmutableArray<(TextBlock Name, TextBlock Description)> _arguments;
    readonly ImmutableArray<(TextBlock Names, TextBlock Description)> _options;
    readonly ImmutableArray<(TextBlock Names, TextBlock Description)> _flags;

    public CommandHelp( Command c )
    {
        _command = c;
        _commandPath = TextBlock.FromText( c.CommandPath );
        _description = TextBlock.FromText( c.Description );
        // Arguments.
        var args = new (TextBlock, TextBlock)[c.Arguments.Length];
        for( int i = 0; i < args.Length; i++ )
        {
            var a = c.Arguments[i];
            args[i] = (TextBlock.FromText( $"<{a.Name}>" ), TextBlock.FromText( a.Description ));
        }
        _arguments = ImmutableCollectionsMarshal.AsImmutableArray( args );
        _commandPathAndArgs = _commandPath.AddRight( _arguments.Select( a => a.Name.Box( paddingLeft: 1 ) ) );
        // Options.
        _options = ToRenderableOptions( c.Options );
        // Flags.
        _flags = ToRenderableFlags( c.Flags );
    }

    public static ImmutableArray<(TextBlock Names, TextBlock Description)> ToRenderableFlags( ImmutableArray<(ImmutableArray<string> Names, string Description)> flags1 )
    {
        var flags = new (TextBlock, TextBlock)[flags1.Length];
        for( int i = 0; i < flags.Length; i++ )
        {
            var f = flags1[i];
            flags[i] = (TextBlock.FromText( string.Join( ", ", f.Names ) ), TextBlock.FromText( f.Description ));
        }
        return ImmutableCollectionsMarshal.AsImmutableArray( flags );
    }

    public static ImmutableArray<(TextBlock Names, TextBlock Description)> ToRenderableOptions( ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> options )
    {
        var aOpts = new (TextBlock, TextBlock)[options.Length];
        for( int i = 0; i < aOpts.Length; i++ )
        {
            var o = options[i];
            aOpts[i] = (TextBlock.FromText( string.Join( ", ", o.Names ) ), TextBlock.FromText( o.Multiple ? "[Multiple] " + o.Description : o.Description ) );
        }
        return ImmutableCollectionsMarshal.AsImmutableArray( aOpts );
    }

    public Command Command => _command;

    public TextBlock CommandPath => _commandPath;

    public IRenderable CommandPathAndArgs => _commandPathAndArgs;

    public TextBlock Description => _description;

    public ImmutableArray<(TextBlock Name, TextBlock Description)> Arguments => _arguments;

    public ImmutableArray<(TextBlock Names, TextBlock Description)> Options => _options;

    public ImmutableArray<(TextBlock Names, TextBlock Description)> Flags => _flags;

}
