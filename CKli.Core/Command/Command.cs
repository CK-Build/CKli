using CK.Core;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Immutable command handler and description.
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
public abstract partial class Command
{
    readonly IPluginTypeInfo? _typeInfo;
    readonly string _commandPath;
    readonly string _description;
    readonly ImmutableArray<(string Name, string Description)> _arguments;
    readonly ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> _options;
    readonly ImmutableArray<(ImmutableArray<string> Names, string Description)> _flags;

    /// <summary>
    /// Initializes a new command with its description.
    /// </summary>
    /// <param name="typeInfo">The type that handles the command.</param>
    /// <param name="commandPath">The whitespace separated command path.</param>
    /// <param name="description">The command description.</param>
    /// <param name="arguments">The required command arguments.</param>
    /// <param name="options">The options.</param>
    /// <param name="flags">The flags.</param>
    protected Command( IPluginTypeInfo? typeInfo,
                       string commandPath,
                       string description,
                       ImmutableArray<(string Name, string Description)> arguments,
                       ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> options,
                       ImmutableArray<(ImmutableArray<string> Names, string Description)> flags )
    {
        Throw.CheckArgument( IsValidCommandPath( commandPath ) );
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
    /// Gets whether this command is disabled: its <see cref="PluginTypeInfo"/> is disabled.
    /// </summary>
    public bool IsDisabled => _typeInfo != null && _typeInfo.Status.IsDisabled();

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

    /// <summary>
    /// Gets the interactive mode.
    /// </summary>
    public virtual InteractiveMode InteractiveMode => InteractiveMode.Both;

    /// <summary>
    /// Returns "[CKli] commad path".
    /// This applies to intrinsic CKli commands and is overridden by command implemented by plugin.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"[CKli] {_commandPath}";

    /// <summary>
    /// Command handler implementation. <paramref name="cmdLine"/> is ready to be consumed, the number of
    /// required <see cref="Arguments"/> is guaranteed to exist in the <paramref name="cmdLine"/> (and must be
    /// consumed).
    /// <para>
    /// <see cref="CommandLineArguments.Close(IActivityMonitor)"/> must ALWAYS be called
    /// before executing the command and the execution skipped if it returned false.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The basic command context.</param>
    /// <param name="cmdLine">The matching command line.</param>
    /// <returns>True on success, false on error. Errors must be logged.</returns>
    internal protected abstract ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine );


    /// <summary>
    /// A valid command path is a single white separated list of lower case identifiers with optional leading '*' and/or trailing '*'.
    /// Minus '-' can be in or end identifiers but cannot start them (kebab-case).
    /// </summary>
    /// <param name="commandPath">The command path.</param>
    /// <returns>True if this is a valid command path.</returns>
    public static bool IsValidCommandPath( string commandPath ) => ValidCommandPath().IsMatch( commandPath );

    /// <summary>
    /// Used by code generator. This is implemented here to maintain the correspondence between the <see cref="IsValidCommandPath(string)"/>
    /// definition and this rewriting.
    /// </summary>
    /// <param name="b">The target string builder.</param>
    /// <param name="command">The command for which the command path must be written as an identifier.</param>
    /// <returns>The string builder.</returns>
    public static StringBuilder WriteCommandPathAsIdentifier( StringBuilder b, Command command )
    {
        foreach( var c in command.CommandPath )
        {
            b.Append( c switch
            {
                ' ' => '＿', // Fullwidth Low Line (U+FF3F)
                '-' => '_',  // Underscore (U+005F)
                '*' => 'ж',  // Cyrillic Small Letter Zhe (U+0436)
                _ => c
            } );
        }
        return b;
    }

    [GeneratedRegex( """^(?!-)\*?[a-z][a-z0-9-]*\*?( (?!-)\*?[a-z][a-z0-9-]*\*?)*$""", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture )]
    private static partial Regex ValidCommandPath();
}
