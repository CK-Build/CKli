using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Constructor parameter of a primary plugin.
/// </summary>
public sealed class PrimaryPluginContext
{
    readonly World _world;
    readonly PluginInfo _pluginInfo;
    readonly PluginConfiguration _configuration;
    PluginConfigurationEditor? _configurationEditor;

    /// <summary>
    /// Constructor used by reflection based plugins.
    /// </summary>
    /// <param name="pluginInfo">The plugin information.</param>
    /// <param name="configuration">The plugin configuration.</param>
    /// <param name="world">The world that initializes the plugin.</param>
    public PrimaryPluginContext( PluginInfo pluginInfo, XElement configuration, World world )
    {
        _pluginInfo = pluginInfo;
        _configuration = new PluginConfiguration( configuration );
        _world = world;
    }

    /// <summary>
    /// Constructor used by compiled plugins.
    /// </summary>
    /// <param name="pluginInfo">The plugin information.</param>
    /// <param name="pluginsConfiguration">The plugins configuration.</param>
    /// <param name="world">The world that initializes the plugin.</param>
    public PrimaryPluginContext( PluginInfo pluginInfo,
                                 IReadOnlyDictionary<XName, (XElement Config, bool IsDisabled)> pluginsConfiguration,
                                 World world )
        : this( pluginInfo, pluginsConfiguration[pluginInfo.GetXName()].Config, world )
    {
    }

    /// <summary>
    /// Gets the world.
    /// </summary>
    public World World => _world;

    /// <summary>
    /// Gets the plugin info.
    /// </summary>
    public PluginInfo PluginInfo => _pluginInfo;

    /// <summary>
    /// Gets the plugin configuration.
    /// This must not be altered otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// <para>
    /// Use the <see cref="ConfigurationEditor"/> to edit this configuration or a per Repo configuration.
    /// </para>
    /// </summary>
    public PluginConfiguration Configuration => _configuration;

    /// <summary>
    /// Gets the non null plugin configuration for a given <see cref="Repo"/> if it exists.
    /// </summary>
    /// <param name="repo">The World's repo for which the per Repo plugin configuration must be obtained.</param>
    /// <returns>The plugin configuration or null.</returns>
    public PluginConfiguration? GetConfigurationFor( Repo repo )
    {
        var e = repo._configuration.Element( _pluginInfo.GetXName() );
        return e != null ? new PluginConfiguration( e ) : default;
    }

    /// <summary>
    /// Gets the editor that can be used to edit the plugin configuration and/or per Repo plugin configurations.
    /// </summary>
    public PluginConfigurationEditor ConfigurationEditor
    {
        get
        {
            return _configurationEditor ??= new PluginConfigurationEditor( PluginInfo, World.DefinitionFile, Configuration.XElement );
        }
    }

}
