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
    public static readonly CommandNamespace Empty = new CommandNamespace( new Dictionary<string, Command?>() );

    readonly Dictionary<string, Command?> _commands;

    internal CommandNamespace( Dictionary<string, Command?> commands )
    {
        _commands = commands;
    }

    /// <summary>
    /// Initializes a new command namespace without checks.
    /// This is used by compiled plugins (and by intrinsic CKli commands in Release).
    /// </summary>
    /// <param name="commands">The already built command namespace.</param>
    public static CommandNamespace UnsafeCreate( Dictionary<string, Command?> commands ) => new CommandNamespace( commands );

    /// <summary>
    /// Finds a command from its command path.
    /// </summary>
    /// <param name="commandPath">The command path.</param>
    /// <returns>The command if it exists, null otherwise.</returns>
    public Command? Find( string commandPath ) => _commands.GetValueOrDefault( commandPath );

    /// <summary>
    /// Gets a list of <see cref="CommandHelp"/> from an optional command path.
    /// <para>
    /// This is public mainly for tests.
    /// </para>
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="helpPath">The optional help path. When null, all commands are considered.</param>
    /// <param name="otherCommands">Optional secondary namespace from wich commands must be merged.</param>
    /// <returns>A list of commands that should display their definition.</returns>
    public List<CommandHelp> GetForHelp( ScreenType screenType, string? helpPath, CommandNamespace? otherCommands )
    {
        // No optimization here. This is the help.
        IEnumerable<Command> commands = _commands.Where( kv => kv.Value != null ).Select( kv => kv.Value )!;
        if( otherCommands  != null ) commands = commands.Concat( otherCommands._commands.Where( kv => kv.Value != null ).Select( kv => kv.Value ) )!;
        if( !string.IsNullOrEmpty( helpPath ) )
        {
            var prefix = helpPath + ' ';
            commands = commands.Where( c => c.CommandPath == helpPath || c.CommandPath.StartsWith( prefix ) );
        }
        return commands.OrderBy( c => c.CommandPath ).Select( c => new CommandHelp( screenType, c ) ).ToList();
    }

    /// <summary>
    /// Gets the commands and the pure namespace entries.
    /// </summary>
    public IReadOnlyDictionary<string, Command?> Namespace => _commands;

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
        var sArgs = cmdLine.InitialArguments;
        if( sArgs.Length == 0 ) return;

        int pathCount = 0;
        string nextPath = sArgs[0];
        var b = new StringBuilder( nextPath );
        while( _commands.TryGetValue( nextPath, out var next ) )
        {
            path = nextPath;
            if( next != null )
            {
                cmd = next;
            }
            if( ++pathCount == sArgs.Length )
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
