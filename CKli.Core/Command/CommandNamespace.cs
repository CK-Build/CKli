using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    /// <param name="helpPath">
    /// The optional help path. When null or empty, only the depth 1 items are returned (the collapsed
    /// map of the whole command surface): see <see cref="GetCollapsedForHelp"/>.
    /// </param>
    /// <param name="otherCommands">Optional secondary namespace from which commands must be merged.</param>
    /// <returns>A list of commands that should display their definition.</returns>
    public List<CommandHelp> GetForHelp( ScreenType screenType, string? helpPath, CommandNamespace? otherCommands )
    {
        // No help path: the top level is the only help that is unusable in full (55 commands, 382 lines).
        if( string.IsNullOrEmpty( helpPath ) ) return GetCollapsedForHelp( screenType, otherCommands );
        // A help path always displays its whole subtree: the biggest namespace of this stack is 58 lines.
        // No optimization here. This is the help.
        IEnumerable<Command> commands = _commands.Values.OfType<Command>();
        if( otherCommands  != null ) commands = commands.Concat( otherCommands._commands.Values.OfType<Command>() );
        var prefix = helpPath + ' ';
        commands = commands.Where( c => c.CommandPath == helpPath || c.CommandPath.StartsWith( prefix ) );
        var result = commands.OrderBy( c => c.CommandPath, CommandNamespaceItem.PathComparer.Default )
                             .Select( c => new CommandHelp( screenType, c ) )
                             .ToList();
        // When the help is about a namespace, its own description (if any) heads the list.
        var describedNamespace = GetDescribedNamespace( helpPath, otherCommands );
        if( describedNamespace != null )
        {
            result.Insert( 0, new CommandHelp( screenType, describedNamespace ) );
        }
        return result;
    }

    /// <summary>
    /// Gets the depth 1 items: the commands and the namespaces of the root, each namespace carrying
    /// the names of its children so that the collapsed help remains a map of the whole command surface.
    /// <para>
    /// This is public mainly for tests.
    /// </para>
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="otherCommands">Optional secondary namespace from which items must be merged.</param>
    /// <returns>The depth 1 command helps.</returns>
    public List<CommandHelp> GetCollapsedForHelp( ScreenType screenType, CommandNamespace? otherCommands )
    {
        var paths = new HashSet<string>( _commands.Keys );
        if( otherCommands != null ) paths.UnionWith( otherCommands._commands.Keys );
        var result = new List<CommandHelp>();
        foreach( var path in paths.Where( p => !p.Contains( ' ' ) ).Order( CommandNamespaceItem.PathComparer.Default ) )
        {
            // A path cannot be a command here and a namespace there: CommandCollector refuses a
            // [CommandPath] that is an intrinsic CKli command.
            var cmd = Find( path ) ?? otherCommands?.Find( path );
            if( cmd != null )
            {
                result.Add( new CommandHelp( screenType, cmd ) );
            }
            else
            {
                var ns = GetDescribedNamespace( path, otherCommands ) ?? new CommandNamespaceItem( path, [] );
                result.Add( new CommandHelp( screenType, ns, GetChildNames( path, otherCommands ) ) );
            }
        }
        return result;
    }

    /// <summary>
    /// Gets the ordered names (the last segment only) of the direct children of a namespace, from
    /// this namespace and an optional other one.
    /// </summary>
    /// <param name="path">The parent namespace path.</param>
    /// <param name="otherCommands">Optional secondary namespace.</param>
    /// <returns>The ordered child names.</returns>
    public ImmutableArray<string> GetChildNames( string path, CommandNamespace? otherCommands )
    {
        var children = new HashSet<string>( _commands.Keys.Where( IsChild ) );
        if( otherCommands != null ) children.UnionWith( otherCommands._commands.Keys.Where( IsChild ) );
        return [.. children.Order( CommandNamespaceItem.PathComparer.Default )
                           .Select( p => p.Substring( path.Length + 1 ) )];

        bool IsChild( string p ) => p.Length > path.Length
                                    && p.StartsWith( path, StringComparison.Ordinal )
                                    && p[path.Length] == ' '
                                    && !p.AsSpan( path.Length + 1 ).Contains( ' ' );
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
