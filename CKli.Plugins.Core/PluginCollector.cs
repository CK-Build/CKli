using CKli.Core;

namespace CKli.Plugins;

/// <summary>
/// Hides the concrete <see cref="IPluginCollector"/> that is currently implemented.
/// </summary>
public static class PluginCollector
{
    /// <summary>
    /// Creates the collector that static Register method uses.
    /// </summary>
    /// <param name="context">The plugins collector context.</param>
    /// <returns>The collector to use.</returns>
    public static IPluginCollector Create( PluginCollectorContext context )
    {
        return new ReflectionPluginCollector( context.PluginsConfiguration );
    }
}
