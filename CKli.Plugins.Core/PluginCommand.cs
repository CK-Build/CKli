using CK.Core;
using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// Base class for command implemented by plugins.
/// </summary>
public abstract class PluginCommand : CommandDescription
{
    readonly string _methodName;
    readonly MethodAsyncReturn _returnType;
    internal object? _instance;

    protected PluginCommand( IPluginTypeInfo typeInfo,
                             string commandPath,
                             string description,
                             ImmutableArray<(string Name, string Description)> arguments,
                             ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> options,
                             ImmutableArray<(ImmutableArray<string> Names, string Description)> flags,
                             string methodName,
                             MethodAsyncReturn returnType )
        : base( typeInfo, commandPath, description, arguments, options, flags )
    {
        _methodName = methodName;
        _returnType = returnType;
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
