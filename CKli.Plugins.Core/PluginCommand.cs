using CK.Core;
using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// Base class for command implemented by plugins.
/// </summary>
public abstract class PluginCommand : Command
{
    readonly string _methodName;
    readonly MethodAsyncReturn _returnType;
    internal object? _instance;
    readonly int _idxCKliEnvParameter;
    readonly int _idxCmdLineParameter;

    /// <summary>
    /// Initializes a new <see cref="PluginCommand"/>.
    /// </summary>
    /// <param name="typeInfo">The type info.</param>
    /// <param name="commandPath">The command path.</param>
    /// <param name="description">The command description.</param>
    /// <param name="idxCKliEnvParameter">
    /// The <see cref="CKliEnv"/> parameter index or -1. Can only be -1, 1 (or 2 if <paramref name="idxCKliEnvParameter"/> is 1).
    /// </param>
    /// <param name="idxCmdLineParameter">
    /// The <see cref="CommandLineArguments"/> parameter index or -1. Can only be -1, 1 or 2.
    /// Can be 2 only if <paramref name="idxCKliEnvParameter"/> is 1.
    /// </param>
    /// <param name="arguments">The command arguments.</param>
    /// <param name="options">The command options.</param>
    /// <param name="flags">The command flags.</param>
    /// <param name="methodName">The method name that implements the command.</param>
    /// <param name="returnType">The command return type.</param>
    protected PluginCommand( IPluginTypeInfo typeInfo,
                             string commandPath,
                             string description,
                             int idxCKliEnvParameter,
                             int idxCmdLineParameter,
                             ImmutableArray<(string Name, string Description)> arguments,
                             ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> options,
                             ImmutableArray<(ImmutableArray<string> Names, string Description)> flags,
                             string methodName,
                             MethodAsyncReturn returnType )
        : base( typeInfo, commandPath, description, arguments, options, flags )
    {
        _methodName = methodName;
        _returnType = returnType;
        _idxCKliEnvParameter = idxCKliEnvParameter;
        _idxCmdLineParameter = idxCmdLineParameter;
    }

    /// <summary>
    /// Gets the type that implements the command method.
    /// </summary>
    public new IPluginTypeInfo PluginTypeInfo => base.PluginTypeInfo!;

    /// <summary>
    /// Gets the name of the handler method.
    /// </summary>
    public string MethodName => _methodName;

    /// <summary>
    /// Gets the type of return of the method.
    /// </summary>
    public MethodAsyncReturn ReturnType => _returnType;

    /// <summary>
    /// Gets the parameter index of <see cref="CKliEnv"/> in <see cref="MethodName"/> parameters.
    /// This can be -1 (no context), 1 or 2 (the first parameter is always the <see cref="IActivityMonitor"/>).
    /// </summary>
    public int IdxCKliEnvParameter => _idxCKliEnvParameter;

    /// <summary>
    /// Gets the parameter index of <see cref="CommandLineArguments"/> in <see cref="MethodName"/> parameters.
    /// This can be -1 (for a regular command method signature), 1 or 2 (the first parameter is always the <see cref="IActivityMonitor"/>).
    /// When the CommandLineArguments is used, no other parameter except <see cref="CKliEnv"/> exist. 
    /// </summary>
    public int IdxCmdLineParameter => _idxCmdLineParameter;

    /// <summary>
    /// Gets the instance that implements the command.
    /// </summary>
    protected object Instance
    {
        get
        {
            Throw.DebugAssert( _instance != null );
            return _instance;
        }
    }

    /// <summary>
    /// Gets the full method name that implements this command.
    /// </summary>
    /// <returns>The method name.</returns>
    public sealed override string ToString() => $"{PluginTypeInfo.TypeName}.{_methodName}";
}
