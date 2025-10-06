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
    readonly Dictionary<string, Command?> _commands;

    internal CommandNamespace( Dictionary<string, Command?> commands )
    {
        _commands = commands;
    }

    /// <summary>
    /// Finds a command from its command path.
    /// </summary>
    /// <param name="commandPath">The command path.</param>
    /// <returns>The command if it exists, null otherwise.</returns>
    public Command? Find( string commandPath ) => _commands.GetValueOrDefault( commandPath );


    internal Dictionary<string, Command?> InternalCommands => _commands;

    internal void Clear() => _commands.Clear();

    internal List<Command> GetForHelp( string? helpPath, CommandNamespace? otherCommands )
    {
        // No optimization here. This is the help.
        IEnumerable<Command> commands = _commands.Where( kv => kv.Value != null ).Select( kv => kv.Value )!;
        if( otherCommands  != null ) commands = commands.Concat( otherCommands._commands.Where( kv => kv.Value != null ).Select( kv => kv.Value ) )!;
        if( !string.IsNullOrEmpty( helpPath ) )
        {
            var prefix = helpPath + ' ';
            commands = commands.Where( c => c.CommandPath == helpPath || c.CommandPath.StartsWith( prefix ) );
        }
        return commands.OrderBy( c => c.CommandPath ).ToList();
    }

    internal bool TryFindForExecution( IActivityMonitor monitor, CommandLineArguments cmdLine, out Command? cmd, out string? helpPath )
    {
        LocateCommand( cmdLine, out cmd, out helpPath );
        if( cmd == null )
        {
            cmd = null;
            return true;
        }
        if( cmd.Arguments.Length > cmdLine.RemainingCount )
        {
            monitor.Error( $"Command '{cmd.CommandPath}' requires {cmd.Arguments.Length} arguments." );
            return false;
        }
        return true;
    }

    void LocateCommand( CommandLineArguments cmdLine, out Command? cmd, out string? path )
    {
        cmd = null;
        path = null;
        var sArgs = cmdLine.Remaining;
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
        cmdLine.EatPath( pathCount );
    }

}
