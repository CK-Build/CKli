using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// Immutable CKli command description.
/// <para>
/// The command model is rather simple. Arguments are required, options have an associated value
/// and are optional and flags are booleans that defaults to false.
/// </para>
/// </summary>
public sealed class CommandDescription
{
    readonly IPluginTypeInfo? _typeInfo;
    readonly string _fullCommandPath;
    readonly string _description;
    readonly ImmutableArray<(string Name, string Description)> _arguments;
    readonly ImmutableArray<(ImmutableArray<string> Names, string Description)> _options;
    readonly ImmutableArray<(ImmutableArray<string> Names, string Description)> _flags;

    /// <summary>
    /// Initializes a new command description.
    /// </summary>
    /// <param name="typeInfo">The type that handles the command.</param>
    /// <param name="fullCommandPath">The full whitespace separated command path.</param>
    /// <param name="description">The command description.</param>
    /// <param name="arguments">The required command arguments.</param>
    /// <param name="options">The options.</param>
    /// <param name="flags">The flags.</param>
    public CommandDescription( IPluginTypeInfo? typeInfo,
                               string fullCommandPath,
                               string description,
                               ImmutableArray<(string Name, string Description)> arguments,
                               ImmutableArray<(ImmutableArray<string> Names, string Description)> options,
                               ImmutableArray<(ImmutableArray<string> Names, string Description)> flags )
    {
        _typeInfo = typeInfo;
        _fullCommandPath = fullCommandPath;
        _description = description;
        _arguments = arguments;
        _options = options;
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
    /// Gets the command description.
    /// </summary>
    public string Description => _description;

    /// <summary>
    /// Gets the (required) arguments and their description.
    /// </summary>
    public ImmutableArray<(string Name, string Description)> Arguments => _arguments;

    /// <summary>
    /// Gets the options name and description. The first name is the long form ("--configuration"), the
    /// last ones are the short forms ("-c").
    /// <para>
    /// Options are followed by a value.
    /// </para>
    /// </summary>
    public ImmutableArray<(ImmutableArray<string> Names, string Description)> Options => _options;

    /// <summary>
    /// Gets the flags name and description. The first name is the long form ("--extended"), the
    /// last ones are the short forms ("-x").
    /// <para>
    /// Flags are only boolean values that default to false: the presence of the flag sets the
    /// boolean to true.
    /// </para>
    /// </summary>
    public ImmutableArray<(ImmutableArray<string> Names, string Description)> Flags => _flags;
}
