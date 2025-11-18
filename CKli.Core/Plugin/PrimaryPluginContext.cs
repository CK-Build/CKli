using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Constructor parameter of a primary plugin.
/// </summary>
public sealed class PrimaryPluginContext
{
    PluginConfigurationEditor? _configurationEditor;

    /// <summary>
    /// Constructor used by reflection based plugins.
    /// </summary>
    /// <param name="pluginInfo">The plugin information.</param>
    /// <param name="configuration">The plugin configuration.</param>
    /// <param name="world">The world that initializes the plugin.</param>
    public PrimaryPluginContext( PluginInfo pluginInfo, XElement configuration, World world )
    {
        PluginInfo = pluginInfo;
        Configuration = configuration;
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
        Configuration = pluginsConfiguration[pluginInfo.PluginName].Config;
        World = world;
    }

    /// <summary>
    /// Gets the world.
    /// </summary>
    public World World { get; }

    /// <summary>
    /// Gets the plugin info.
    /// </summary>
    public PluginInfo PluginInfo { get; }

    /// <summary>
    /// Gets the xml configuration element.
    /// This must not be altered otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// <para>
    /// Use the <see cref="ConfigurationEditor"/> to edit the configuration.
    /// </para>
    /// </summary>
    public XElement Configuration { get; }

    /// <summary>
    /// Gets the editor that can be used to edit the plugin configuration.
    /// </summary>
    public PluginConfigurationEditor ConfigurationEditor
    {
        get
        {
            return _configurationEditor ??= new PluginConfigurationEditor( PluginInfo, World.DefinitionFile, Configuration );
        }
    }
}
