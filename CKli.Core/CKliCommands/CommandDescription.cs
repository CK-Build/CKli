using CK.Core;
using System;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace CKli.Core;

/// <summary>
/// Immutable command description.
/// <para>
/// This command model supports 3 kind of parameters that must appear in the following order on the command line:
/// <list type="number">
/// <item>
///     <term>Arguments</term>
///     <description>
///         Required arguments that map to string method parameters. The exact number must appear first on the command line.
///     </description>
/// </item>
/// <item>
///     <term>Options</term>
///     <description>
///         Optional named value that map to optional string method parameters (parameter must have a null default)
///         or an optional array of strings (parameter must have a null default).
///         <para>
///         When the method parameter is an array of strings, multiple occurences of the option names can appear on the command line.
///         </para>
///     </description>
/// </item>
/// <item>
///     <term>Flags</term>
///     <description>
///         Optional names that map to optional boolean method parameters that should have a false default value.
///         The value is true when the flag name appears.
///         </description>
///  </item>
/// </list>
/// </para>
/// </summary>
public partial class CommandDescription
{
    readonly IPluginTypeInfo? _typeInfo;
    readonly string _commandPath;
    readonly string _description;
    readonly ImmutableArray<(string Name, string Description)> _arguments;
    readonly ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> _options;
    readonly ImmutableArray<(ImmutableArray<string> Names, string Description)> _flags;

    /// <summary>
    /// Initializes a new command description.
    /// </summary>
    /// <param name="typeInfo">The type that handles the command.</param>
    /// <param name="commandPath">The whitespace separated command path.</param>
    /// <param name="description">The command description.</param>
    /// <param name="arguments">The required command arguments.</param>
    /// <param name="options">The options.</param>
    /// <param name="flags">The flags.</param>
    public CommandDescription( IPluginTypeInfo? typeInfo,
                               string commandPath,
                               string description,
                               ImmutableArray<(string Name, string Description)> arguments,
                               ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> options,
                               ImmutableArray<(ImmutableArray<string> Names, string Description)> flags )
    {
        Throw.CheckArgument( ValidCommandPath().IsMatch( commandPath ) );
        Throw.CheckNotNullArgument( description );
        _typeInfo = typeInfo;
        _commandPath = commandPath;
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
    public string CommandPath => _commandPath;

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
    public ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> Options => _options;

    /// <summary>
    /// Gets the flags name and description. The first name is the long form ("--extended"), the
    /// last ones are the short forms ("-x").
    /// <para>
    /// Flags are only boolean values that default to false: the presence of the flag sets the
    /// boolean to true.
    /// </para>
    /// </summary>
    public ImmutableArray<(ImmutableArray<string> Names, string Description)> Flags => _flags;

    [GeneratedRegex( "^[a-z]+(?: [a-z]+)$" )]
    private static partial Regex ValidCommandPath();
}

public abstract class CKliCommand : CommandDescription
{
    private protected CKliCommand( IPluginTypeInfo? typeInfo,
                                   string commandPath,
                                   string description,
                                   ImmutableArray<(string Name, string Description)> arguments,
                                   ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> options,
                                   ImmutableArray<(ImmutableArray<string> Names, string Description)> flags )
        : base( typeInfo, commandPath, description, arguments, options, flags )
    {
    }

    internal abstract int HandleCommand( IActivityMonitor monitor,
                                         ISecretsStore secretsStore,
                                         NormalizedPath path,
                                         string[] args );
}

/// <summary>
/// Clone command.
/// </summary>
public sealed class CKliClone : CKliCommand
{
    internal CKliClone()
        : base( null,
                "clone",
                "Clones a Stack and all its current World repositories in the current directory.",
                [("stackUrl", "The url stack repository to clone from. The repository name must end with '-Stack'.")],
                [],
                [
                    (["--private"],"Indicates a private repository. A Personal Access Token (or any other secret) is required."),
                    (["--allow-duplicate"],"Allows a Stack that already exists locally to be cloned."),
                ] ) 
    {
    }

    internal override int HandleCommand( IActivityMonitor monitor, ISecretsStore secretsStore, NormalizedPath path, string[] args )
    {
        if( Uri.TryCreate( new string( s ), UriKind.Absolute, out var uri ) )
    }

    /// <summary>
    /// Clones a Stack and all its current world repositories in the <paramref name="path"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="stackUrl">The url stack repository to clone from. The repository name must end with '-Stack'.</param>
    /// <param name="private">Indicates a private repository. A Personal Access Token (or any other secret) is required.</param>
    /// <param name="allowDuplicate">Allows a stack that already exists locally to be cloned.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int Clone( IActivityMonitor monitor,
                             ISecretsStore secretsStore,
                             NormalizedPath path,
                             Uri stackUrl,
                             bool @private = false,
                             bool allowDuplicate = false )
    {
        using( var stack = StackRepository.Clone( monitor, secretsStore, stackUrl, !@private, path, allowDuplicate ) )
        {
            return stack != null
                    ? 0
                    : -1;
        }
    }

}
