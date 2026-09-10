using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CKli;

/// <summary>
/// Immutable command namespace.
/// </summary>
public sealed class CommandNamespace
{
    /// <summary>
    /// An empty command namespace.
    /// </summary>
    public static readonly CommandNamespace Empty = new CommandNamespace( new Dictionary<string, CommandNamespaceItem>() );

    readonly Dictionary<string, CommandNamespaceItem> _commands;

    internal CommandNamespace( Dictionary<string, CommandNamespaceItem> commands )
    {
        _commands = commands;
    }

    /// <summary>
    /// Initializes a new command namespace without checks.
    /// This is used by compiled plugins (and by intrinsic CKli commands in Release).
    /// </summary>
    /// <param name="commands">The already built command namespace.</param>
    public static CommandNamespace UnsafeCreate( Dictionary<string, CommandNamespaceItem> commands ) => new CommandNamespace( commands );

    /// <summary>
    /// Finds a command from its command path. A pure namespace is not a command: null is returned
    /// for it (use <see cref="FindItem(string)"/> to obtain it).
    /// </summary>
    /// <param name="commandPath">The command path.</param>
    /// <returns>The command if it exists, null otherwise.</returns>
    public Command? Find( string commandPath ) => _commands.GetValueOrDefault( commandPath ) as Command;

    /// <summary>
    /// Finds a command or a pure namespace from its path.
    /// </summary>
    /// <param name="commandPath">The command or namespace path.</param>
    /// <returns>The item if it exists, null otherwise.</returns>
    public CommandNamespaceItem? FindItem( string commandPath ) => _commands.GetValueOrDefault( commandPath );

    /// <summary>
    /// Gets a list of <see cref="CommandHelp"/> from an optional command path.
    /// <para>
    /// This is public mainly for tests.
    /// </para>
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="helpPath">The optional help path. When null, all commands are considered.</param>
    /// <param name="otherCommands">Optional secondary namespace from which commands must be merged.</param>
    /// <returns>A list of commands that should display their definition.</returns>
    public List<CommandHelp> GetForHelp( ScreenType screenType, string? helpPath, CommandNamespace? otherCommands )
    {
        // No optimization here. This is the help.
        IEnumerable<Command> commands = _commands.Values.OfType<Command>();
        if( otherCommands  != null ) commands = commands.Concat( otherCommands._commands.Values.OfType<Command>() );
        CommandNamespaceItem? describedNamespace = null;
        if( !string.IsNullOrEmpty( helpPath ) )
        {
            var prefix = helpPath + ' ';
            commands = commands.Where( c => c.CommandPath == helpPath || c.CommandPath.StartsWith( prefix ) );
            describedNamespace = GetDescribedNamespace( helpPath, otherCommands );
        }
        var result = commands.OrderBy( c => c.CommandPath ).Select( c => new CommandHelp( screenType, c ) ).ToList();
        // When the help is about a namespace, its own description (if any) heads the list.
        if( describedNamespace != null )
        {
            result.Insert( 0, new CommandHelp( screenType, describedNamespace ) );
        }
        return result;
    }

    /// <summary>
    /// Gets the described namespace item for a path, merging the parts of the two namespaces when
    /// both describe it. Null if the path is a command, is unknown or has no description at all.
    /// </summary>
    CommandNamespaceItem? GetDescribedNamespace( string helpPath, CommandNamespace? otherCommands )
    {
        var mine = _commands.GetValueOrDefault( helpPath );
        if( mine is Command ) return null;
        var other = otherCommands?._commands.GetValueOrDefault( helpPath );
        if( other is Command ) other = null;
        if( mine == null || mine.DescriptionParts.Length == 0 ) return other?.DescriptionParts.Length > 0 ? other : null;
        if( other == null || other.DescriptionParts.Length == 0 ) return mine;
        // Reorder: "mine" is the World's namespace and "other" the CKli one, but the CKli parts
        // must come first, exactly as when a single namespace is built.
        return new CommandNamespaceItem( helpPath,
                                         CommandNamespaceItem.OrderParts( mine.DescriptionParts.AddRange( other.DescriptionParts ) ) );
    }

    /// <summary>
    /// Gets the commands and the pure namespace entries: a <see cref="CommandNamespaceItem"/> that is
    /// not a <see cref="Command"/> is a pure namespace.
    /// </summary>
    public IReadOnlyDictionary<string, CommandNamespaceItem> Namespace => _commands;

    internal void Clear() => _commands.Clear();

    internal bool TryFindForExecution( IActivityMonitor monitor, CommandLineArguments cmdLine, out string? helpPath )
    {
        LocateCommand( cmdLine, out helpPath );
        if( cmdLine.FoundCommand == null )
        {
            return true;
        }
        if( !cmdLine.HasHelp && cmdLine.FoundCommand.Arguments.Length > cmdLine.RemainingCount )
        {
            monitor.Error( $"Command '{cmdLine.FoundCommand.CommandPath}' requires {cmdLine.FoundCommand.Arguments.Length} arguments." );
            return false;
        }
        return true;
    }

    void LocateCommand( CommandLineArguments cmdLine, out string? path )
    {
        Command? cmd = null;
        path = null;
        // The command must be located on the remaining arguments, not on the InitialArguments: the global
        // "--path"/"-p", "i"/"interactive", "--ckli-debug" and "--ckli-screen" have been eaten and are no more
        // here. Using the InitialArguments made any of them, when placed before the command (as documented for
        // "--path"), hide the command: sArgs[0] was the option itself and no command path could match.
        // SetFoundCommand( cmd, pathCount ) below also removes pathCount arguments from these very same
        // remaining arguments: both must be indexed the same way.
        var sArgs = cmdLine.RemainingArguments;
        if( sArgs.Count == 0 ) return;

        int pathCount = 0;
        string nextPath = sArgs[0];
        var b = new StringBuilder( nextPath );
        while( _commands.TryGetValue( nextPath, out var next ) )
        {
            path = nextPath;
            if( next is Command c )
            {
                cmd = c;
            }
            if( ++pathCount == sArgs.Count )
            {
                break;
            }

            b.Append( ' ' ).Append( sArgs[pathCount] );
            nextPath = b.ToString();
        }
        if( cmd != null )
        {
            cmdLine.SetFoundCommand( cmd, pathCount );
        }
    }
}
