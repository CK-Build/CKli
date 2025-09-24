using CKli.Core;

namespace CKli.Plugins;

/// <summary>
/// Abstracts the concrete <see cref="IPluginCollector"/> that is currently implemented.
/// </summary>
public static class PluginCollector
{
    /// <summary>
    /// Creates the collector that static Register method uses.
    /// </summary>
    /// <param name="ctx">The context of the plugins.</param>
    /// <returns>The collector to use.</returns>
    public static IPluginCollector Create( PluginCollectorContext ctx )
    {
        return new ReflectionBasedPluginCollector( ctx );
    }
}
