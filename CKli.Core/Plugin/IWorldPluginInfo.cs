namespace CKli.Core;

/// <summary>
/// Provides fundamental plugin informations.
/// </summary>
public interface IWorldPluginInfo
{
    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the status flags.
    /// </summary>
    WorldPluginStatus Status { get; }
}

