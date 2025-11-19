using CKli.Core;

namespace CKli.Plugins;

/// <summary>
/// Only used by compiled plugins.
/// </summary>
public sealed class PluginTypeInfo : IPluginTypeInfo
{
    /// <summary>
    /// Initializes a new immutable <see cref="PluginTypeInfo"/>.
    /// </summary>
    /// <param name="plugin">The plugin information.</param>
    /// <param name="typeName">The type name.</param>
    /// <param name="isPrimary">Whether this is a primary or a support plugin.</param>
    /// <param name="status">The plugin status.</param>
    /// <param name="activationIndex">The activation index.</param>
    public PluginTypeInfo( PluginInfo plugin, string typeName, bool isPrimary, int status, int activationIndex )
    {
        Plugin = plugin;
        TypeName = typeName;
        IsPrimary = isPrimary;
        ActivationIndex = activationIndex;
        Status = (PluginStatus)status;
    }

    /// <inheritdoc />
    public PluginInfo Plugin { get; }

    /// <inheritdoc />
    public string TypeName { get; }

    /// <inheritdoc />
    public bool IsPrimary { get; }

    /// <inheritdoc />
    public PluginStatus Status { get; }

    /// <inheritdoc />
    public int ActivationIndex { get; }

    /// <summary>
    /// Overridden to return the <see cref="TypeName"/>.
    /// </summary>
    /// <returns>The TypeName.</returns>
    public override string ToString() => TypeName;
}
