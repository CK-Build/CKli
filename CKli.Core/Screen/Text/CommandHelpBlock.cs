using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace CKli.Core;

public class CommandHelpBlock
{
    readonly Command _command;
    readonly LineBlock _commandPath;
    readonly ILineRenderable _commandPathAndArgs;
    readonly TextBlock _description;
    readonly ImmutableArray<(WordBlock Name, TextBlock Description)> _arguments;
    readonly ImmutableArray<(TokensBlock Names, TextBlock Description, bool Multiple)> _options;
    readonly ImmutableArray<(TokensBlock Names, TextBlock Description )> _flags;

    public CommandHelpBlock( Command c )
    {
        _command = c;
        _commandPath = new LineBlock( c.CommandPath );
        _description = new TextBlock( c.Description );
        // Arguments.
        var args = new (WordBlock, TextBlock)[c.Arguments.Length];
        for( int i = 0; i < args.Length; i++ )
        {
            var a = c.Arguments[i];
            args[i] = (new WordBlock( $"<{a.Name}>" ), new TextBlock( a.Description ));
        }
        _arguments = ImmutableCollectionsMarshal.AsImmutableArray( args );
        _commandPathAndArgs = _commandPath.AddRight( _arguments.Select( a => a.Name.Box( left: 1 ) ) );
        // Options.
        var opts = new (TokensBlock, TextBlock, bool)[c.Options.Length];
        for( int i = 0; i < opts.Length; i++ )
        {
            var o = c.Options[i];
            opts[i] = (new TokensBlock( o.Names, ", " ), new TextBlock( o.Description ), o.Multiple);
        }
        _options = ImmutableCollectionsMarshal.AsImmutableArray( opts );
        // Flags.
        var flags = new (TokensBlock, TextBlock)[c.Flags.Length];
        for( int i = 0; i < flags.Length; i++ )
        {
            var f = c.Flags[i];
            flags[i] = (new TokensBlock( f.Names, ", " ), new TextBlock( f.Description ));
        }
        _flags = ImmutableCollectionsMarshal.AsImmutableArray( flags );
    }

    public Command Command => _command;

    public LineBlock CommandPath => _commandPath;

    public ILineRenderable CommandPathAndArgs => _commandPathAndArgs;

    public TextBlock Description => _description;

    public ImmutableArray<(WordBlock Name, TextBlock Description)> Arguments => _arguments;

    public ImmutableArray<(TokensBlock Names, TextBlock Description, bool Multiple)> Options => _options;

    public ImmutableArray<(TokensBlock Names, TextBlock Description)> Flags => _flags;

}
