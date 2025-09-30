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
    public PluginTypeInfo( PluginInfo plugin, string typeName, bool isPrimary, int status )
    {
        Plugin = plugin;
        TypeName = typeName;
        IsPrimary = isPrimary;
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

    public override string ToString() => TypeName;
}
