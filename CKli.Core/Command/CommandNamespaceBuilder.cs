using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Linq;

namespace CKli;

/// <summary>
/// <see cref="CommandNamespace"/> builder.
/// </summary>
public sealed class CommandNamespaceBuilder
{
    readonly Dictionary<string, Command?> _commands;
    readonly Dictionary<string, string> _namespaceDescriptions = new();

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
    public CommandNamespace Build() => new CommandNamespace( _commands, _namespaceDescriptions );

    /// <summary>
    /// Adds a description for a namespace path. First-write-wins: if a description
    /// is already set for <paramref name="path"/>, subsequent calls are silently ignored.
    /// </summary>
    /// <param name="path">The namespace path (e.g. "hosting" or "hosting deploy").</param>
    /// <param name="description">The namespace description.</param>
    public void AddNamespaceDescription( string path, string description )
    {
        _namespaceDescriptions.TryAdd( path, description );
    }

    /// <summary>
    /// Adds or throw on already existing command or if the new command is not below a pure
    /// namespace.
    /// </summary>
    /// <param name="c">The command to add.</param>
    public void Add( Command c )
    {
        if( _commands.TryGetValue( c.CommandPath, out var conflict ) )
        {
            if( conflict != null )
            {
                Throw.CKException( $"Invalid command '{c.CommandPath}': this command path already exists." );
            }
            var prefix = c.CommandPath + ' ';
            var paths = _commands.Where( kv => kv.Value != null ).Select( kv => kv.Value!.CommandPath )
                                    .Where( p => p == prefix || p.StartsWith( prefix ) )
                                    .Order();
            Throw.CKException( $"""
                Invalid command '{c.CommandPath}' would hide already registered commands:
                '{paths.Concatenate( "', '" )}'.
                """ );
        }
        // The new command must not appear below an actual command.
        CheckParentNoCommand( GetPath( c.CommandPath ), c );
        _commands.Add( c.CommandPath, c );
    }

    void CheckParentNoCommand( string? commandPath, Command leaf )
    {
        if( commandPath == null ) return;
        CheckParentNoCommand( GetPath( commandPath ), leaf );
        if( _commands.TryGetValue( commandPath, out var parentCommand ) )
        {
            if( parentCommand != null )
            {
                Throw.CKException( $"Command '{leaf.CommandPath}' cannot be defined: '{commandPath}' is an actual command and not a namespace." );
            }
        }
        else
        {
            // Declares the namespace.
            _commands.Add( commandPath, null );
        }
    }

    static string? GetPath( string commandPath )
    {
        int idx = commandPath.LastIndexOf( ' ' );
        Throw.DebugAssert( idx != 0 && idx != commandPath.Length - 1 );
        return idx > 0 ? commandPath.Substring( 0, idx ) : null;
    }
}
