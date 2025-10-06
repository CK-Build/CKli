using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CKli;

/// <summary>
/// <see cref="CommandNamespace"/> builder.
/// </summary>
public sealed class CommandNamespaceBuilder
{
    readonly Dictionary<string, Command?> _commands;

    /// <summary>
    /// Initiaizes a new builder.
    /// </summary>
    public CommandNamespaceBuilder()
    {
        _commands = new Dictionary<string, Command?>();
    }

    /// <summary>
    /// Creates a new immutable <see cref="CommandNamespace"/>.
    /// </summary>
    /// <returns>The commands.</returns>
    public CommandNamespace Build() => new CommandNamespace( _commands );

    /// <summary>
    /// Adds or throw on already existing command.
    /// </summary>
    /// <param name="c">The command to add.</param>
    public void Add( Command c )
    {
        if( !TryAdd( c, out var conflict ) )
        {
            Throw.InvalidOperationException( $"Command '{c.CommandPath}' is already registered." );
        }
    }

    /// <summary>
    /// Tries to add the command.
    /// </summary>
    /// <param name="c">The command to add.</param>
    /// <param name="conflict">The non null existing command on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public bool TryAdd( Command c, [NotNullWhen(false)]out Command? conflict )
    {
        if( _commands.TryGetValue( c.CommandPath, out conflict ) )
        {
            if( conflict == null )
            {
                _commands[c.CommandPath] = c;
                return true;
            }
            return false;
        }
        _commands.Add( c.CommandPath, c );
        var parent = GetPath( c.CommandPath );
        while( parent != null )
        {
            if( !_commands.TryAdd( parent, null ) ) break;
            parent = GetPath( parent );
        }
        return true;
    }

    static string? GetPath( string commandPath )
    {
        int idx = commandPath.LastIndexOf( ' ' );
        Throw.DebugAssert( idx != 0 && idx != commandPath.Length - 1 );
        return idx > 0 ? commandPath.Substring( 0, idx ) : null;
    }
}
