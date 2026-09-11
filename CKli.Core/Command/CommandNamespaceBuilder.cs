using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CKli;

/// <summary>
/// <see cref="CommandNamespace"/> builder.
/// </summary>
public sealed class CommandNamespaceBuilder
{
    readonly Dictionary<string, Command?> _commands;
    readonly Dictionary<string, List<CommandNamespaceItem.DescriptionPart>> _descriptions;

    /// <summary>
    /// Initiaizes a new builder.
    /// </summary>
    public CommandNamespaceBuilder()
    {
        _commands = new Dictionary<string, Command?>();
        _descriptions = new Dictionary<string, List<CommandNamespaceItem.DescriptionPart>>();
    }

    /// <summary>
    /// Creates a new immutable <see cref="CommandNamespace"/>.
    /// <para>
    /// A <see cref="Describe(string, string, string?, Uri?)"/> that doesn't apply to an actual namespace
    /// is warned and ignored: a plugin can describe a namespace that another (currently disabled or
    /// removed) plugin populates.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The commands.</returns>
    public CommandNamespace Build( IActivityMonitor monitor )
    {
        var items = new Dictionary<string, CommandNamespaceItem>( _commands.Count );
        foreach( var (path, cmd) in _commands )
        {
            items.Add( path, cmd ?? new CommandNamespaceItem( path, GetOrderedParts( path ) ) );
        }
        foreach( var (path, parts) in _descriptions )
        {
            if( !_commands.TryGetValue( path, out var cmd ) )
            {
                monitor.Warn( $"""
                    Command namespace '{path}' is described by '{Origins( parts )}' but no command exists in it.
                    This description is ignored.
                    """ );
            }
            else if( cmd != null )
            {
                monitor.Warn( $"""
                    '{path}' is a command, not a command namespace: the description declared by '{Origins( parts )}' is ignored.
                    """ );
            }
        }
        return new CommandNamespace( items );

        static string Origins( List<CommandNamespaceItem.DescriptionPart> parts )
        {
            return parts.Select( p => p.Origin ?? "CKli" ).Distinct().Concatenate( "', '" );
        }
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

    /// <summary>
    /// Appends a description to a command namespace. Multiple plugins can describe the same
    /// namespace: "fix" is populated by CKli.Build.Plugin and CKli.HotZone.Plugin and both can
    /// describe it. Calls can appear before or after the commands that create the namespace.
    /// <para>
    /// The parts are ordered by <paramref name="origin"/> when the <see cref="CommandNamespace"/> is
    /// built (the intrinsic CKli ones first): the plugin activation order never leaks into the help.
    /// </para>
    /// </summary>
    /// <param name="namespacePath">The whitespace separated namespace path.</param>
    /// <param name="description">The description. Must not be null, empty or whitespace.</param>
    /// <param name="summary">Optional one line summary for the collapsed help.</param>
    /// <param name="origin">The full plugin name that declares this description. Null for CKli itself.</param>
    /// <param name="helpUrl">Optional link to an external documentation.</param>
    public void Describe( string namespacePath,
                          string description,
                          string? summary = null,
                          string? origin = null,
                          Uri? helpUrl = null )
    {
        Throw.CheckArgument( Command.IsValidCommandPath( namespacePath ) );
        Throw.CheckNotNullOrWhiteSpaceArgument( description );
        if( !_descriptions.TryGetValue( namespacePath, out var parts ) )
        {
            _descriptions.Add( namespacePath, parts = new List<CommandNamespaceItem.DescriptionPart>() );
        }
        parts.Add( new CommandNamespaceItem.DescriptionPart( description, summary, origin, helpUrl ) );
    }

    ImmutableArray<CommandNamespaceItem.DescriptionPart> GetOrderedParts( string namespacePath )
    {
        return _descriptions.TryGetValue( namespacePath, out var parts )
                ? CommandNamespaceItem.OrderParts( parts )
                : [];
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
