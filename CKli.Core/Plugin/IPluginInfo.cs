using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// Plugin description.
/// </summary>
public interface IPluginInfo
{
    /// <summary>
    /// Gets the full plugin name "CKli.XXX.Plugin" that is the name of the project/package.
    /// </summary>
    string FullPluginName { get; }

    /// <summary>
    /// Gets the short plugin name.
    /// </summary>
    string PluginName { get; }

    /// <summary>
    /// Gets the status flags. <see cref="PluginStatus.DisabledByDependency"/> doesn't apply here.
    /// </summary>
    PluginStatus Status { get; }

}
