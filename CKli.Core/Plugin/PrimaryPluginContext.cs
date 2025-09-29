using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Constructor parameter of a primary plugin.
/// </summary>
public sealed class PrimaryPluginContext
{
    /// <summary>
    /// Constructor used by reflection based plugins.
    /// </summary>
    /// <param name="pluginInfo">The plugin information.</param>
    /// <param name="xmlConfiguration">The plugin configuration.</param>
    /// <param name="world">The world that initializes the plugin.</param>
    public PrimaryPluginContext( PluginInfo pluginInfo, XElement xmlConfiguration, World world )
    {
        PluginInfo = pluginInfo;
        XmlConfiguration = xmlConfiguration;
        World = world;
    }

    /// <summary>
    /// Constructor used by compiled plugins.
    /// </summary>
    /// <param name="pluginInfo">The plugin information.</param>
    /// <param name="pluginsConfiguration">The plugins configuration.</param>
    /// <param name="world">The world that initializes the plugin.</param>
    public PrimaryPluginContext( PluginInfo pluginInfo, IReadOnlyDictionary<string, (XElement Config, bool IsDisabled)> pluginsConfiguration, World world )
    {
        PluginInfo = pluginInfo;
        XmlConfiguration = pluginsConfiguration[pluginInfo.PluginName].Config;
        World = world;
    }

    /// <summary>
    /// Gets the plugin info.
    /// </summary>
    public PluginInfo PluginInfo { get; }

    /// <summary>
    /// Gets the xml configuration element.
    /// This must not be altered otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    public XElement XmlConfiguration { get; }

    /// <summary>
    /// Gets the world.
    /// </summary>
    public World World { get; }
}
