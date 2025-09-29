using CK.Core;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// Plugin description.
/// </summary>
public sealed class PluginInfo
{
    readonly string _fullPluginName;
    readonly string _pluginName;
    readonly PluginStatus _status;
    readonly IReadOnlyList<IPluginTypeInfo> _pluginTypes;

    /// <summary>
    /// Initializes a new PluginInfo.
    /// </summary>
    /// <param name="fullPluginName">The full "CKli.XXX.Plugin" name.</param>
    /// <param name="pluginName">The plugin name.</param>
    /// <param name="status">The status.</param>
    /// <param name="pluginTypes">The types defined by this plugin.</param>
    public PluginInfo( string fullPluginName, string pluginName, PluginStatus status, IReadOnlyList<IPluginTypeInfo> pluginTypes )
    {
        _fullPluginName = fullPluginName;
        _pluginName = pluginName;
        _status = status;
        _pluginTypes = pluginTypes;
    }

    /// <summary>
    /// Gets the full plugin name "CKli.XXX.Plugin" that is the name of the project/package.
    /// </summary>
    public string FullPluginName => _fullPluginName;

    /// <summary>
    /// Gets the short plugin name.
    /// </summary>
    public string PluginName => _pluginName;

    /// <summary>
    /// Gets the status flags. <see cref="PluginStatus.DisabledByDependency"/> doesn't apply here.
    /// </summary>
    public PluginStatus Status => _status;

    /// <summary>
    /// Gets the types defined by this plugin.
    /// </summary>
    public IReadOnlyList<IPluginTypeInfo> PluginTypes => _pluginTypes;

    /// <summary>
    /// Returns the <see cref="FullPluginName"/>.
    /// </summary>
    /// <returns>The full plugin name.</returns>
    public override string ToString() => FullPluginName;

}


