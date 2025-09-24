namespace CKli.Core;

/// <summary>
/// Provides fundamental plugin informations.
/// </summary>
public interface IPluginInfo
{
    /// <summary>
    /// Gets the full plugin name "CKli.XXX.Plugin" that is
    /// the name of the project/package that defines this plugin and also the namespace of the plugin type
    /// (this is checked).
    /// <para>
    /// More than one primary plugin can be implemented in the same project/package: there must be however
    /// one plugin whose type name is "XXXPlugin" that is the only one that surfaces.
    /// If other primary plugins exist they share the same <see cref="XmlConfiguration"/> and are "hiiden".
    /// </para>
    /// </summary>
    string FullPluginName { get; }

    /// <summary>
    /// Gets the plugin type name. This necessarily ends with "Plugin".
    /// </summary>
    string TypeName { get; }

    /// <summary>
    /// Gets the status flags.
    /// </summary>
    PluginStatus Status { get; }
}

