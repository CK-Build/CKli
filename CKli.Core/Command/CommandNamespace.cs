using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace CKli;

/// <summary>
/// Immutable command namespace.
/// </summary>
public sealed class CommandNamespace
{
    readonly Dictionary<string, CommandDescription?> _commands;

    internal CommandNamespace( Dictionary<string, CommandDescription?> commands )
    {
        _commands = commands;
    }

    /// <summary>
    /// Finds a command from its command path.
    /// </summary>
    /// <param name="commandPath">The command path.</param>
    /// <returns>The command if it exists, null otherwise.</returns>
    public CommandDescription? Find( string commandPath ) => _commands.GetValueOrDefault( commandPath );

    internal List<CommandDescription> GetForHelp( string? helpPath )
    {
        // No optimization here. This is the help.
        IEnumerable<CommandDescription> commands = _commands.Where( kv => kv.Value != null ).Select( kv => kv.Value )!;
        if( !string.IsNullOrEmpty( helpPath ) )
        {
            var prefix = helpPath + ' ';
            commands = commands.Where( c => c.CommandPath == helpPath || c.CommandPath.StartsWith( prefix ) );
        }
        return commands.OrderBy( c => c.CommandPath ).ToList();
    }

    internal CommandDescription? FindForExecution( IActivityMonitor monitor, CommandLineArguments cmdLine, out string? helpPath )
    {
        LocateCommand( cmdLine, out CommandDescription? cmd, out helpPath );
        if( cmd == null )
        {
            monitor.Error( "Unable to find command." );
            return null;
        }
        if( cmd.Arguments.Length > cmdLine.RemainingCount )
        {
            monitor.Error( $"Command '{cmd.CommandPath}' requires {cmd.Arguments.Length} arguments." );
            return null;
        }
        return cmd;
    }

    void LocateCommand( CommandLineArguments cmdLine, out CommandDescription? cmd, out string? path )
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
