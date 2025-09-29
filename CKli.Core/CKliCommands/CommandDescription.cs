using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Core;

//public sealed class CommandNamespace
//{
//    public CommandGroup? Find( string name )
//}

//public sealed class CommandGroup
//{
//    readonly string _name;
//    readonly string? _description;
//    readonly List<CommandDescription> _commands;
//    readonly CommandGroup? _parent;
//    int _commandCount;
//
//    internal CommandGroup( CommandGroup? parent, string name,  )
//    {
//    }
//}

/// <summary>
/// Immutable CKli command description.
/// <para>
/// The command model is voluntary very simple. Only arguments and flags defaulting to false are handled.
/// If we can stick to this simple model and never need any "--some-option value" this would be great.
/// </para>
/// </summary>
public sealed class CommandDescription
{
    readonly IPluginTypeInfo? _typeInfo;
    readonly string _fullCommandPath;
    readonly ImmutableArray<(string Name, string Description)> _arguments;
    readonly ImmutableArray<(ImmutableArray<string> Names, string Description)> _flags;

    /// <summary>
    /// Initializes a new command description.
    /// </summary>
    /// <param name="typeInfo">The type that handles the command.</param>
    /// <param name="fullCommandPath">The full whitespace separated command path.</param>
    /// <param name="arguments">The required command arguments.</param>
    /// <param name="flags">The flags.</param>
    public CommandDescription( IPluginTypeInfo? typeInfo,
                               string fullCommandPath,
                               ImmutableArray<(string Name, string Description)> arguments,
                               ImmutableArray<(ImmutableArray<string> Names, string Description)> flags )
    {
        _typeInfo = typeInfo;
        _fullCommandPath = fullCommandPath;
        _arguments = arguments;
        _flags = flags;
    }

    /// <summary>
    /// Get the type.
    /// Null for CKli implemented commands.
    /// </summary>
    public IPluginTypeInfo? PluginTypeInfo => _typeInfo;

    /// <summary>
    /// Gets the full whitespace separated command path.
    /// </summary>
    public string FullCommandPath => _fullCommandPath;

    /// <summary>
    /// Gets the (required) arguments and their description.
    /// </summary>
    public ImmutableArray<(string Name, string Description)> Arguments => _arguments;

    /// <summary>
    /// Gets the flags name and description. The first names should be the short forms ("-x"), the
    /// last one should be the long form ("--extended").
    /// <para>
    /// Flags are only boolean values that default to false: the presence of the flag sets the
    /// boolean to true.
    /// </para>
    /// </summary>
    public ImmutableArray<(ImmutableArray<string> Names, string Description)> Flags => _flags;
}
