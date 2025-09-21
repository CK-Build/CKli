namespace CKli.Core;

public enum WorldPluginStatus
{
    Available,

    /// <summary>
    /// The plugin is disabled in the &lt;Plugins&gt; configuration.
    /// </summary>
    DisabledByConfiguration = 1,

    /// <summary>
    /// The plugin is disabled because at least one of its required dependency
    /// is disabled.
    /// </summary>
    DisabledByDependency = 2,

    /// <summary>
    /// An implementation exists but its name doesn't appear in the &lt;Plugins&gt; configuration.
    /// </summary>
    DisabledByMissingConfiguration = 4,

    /// <summary>
    /// The plugin name appears in the &lt;Plugins&gt; configuration but is not
    /// available.
    /// </summary>
    MissingImplementation = 8

}

public static class WorldPluginStatusExtensions
{
    public static bool IsDisabled( this WorldPluginStatus status )
    {
        return (status & (WorldPluginStatus.DisabledByConfiguration
                         | WorldPluginStatus.DisabledByMissingConfiguration
                         | WorldPluginStatus.DisabledByDependency)) != 0;
    }
}

