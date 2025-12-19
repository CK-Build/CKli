using System.Collections.Generic;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Constructor parameter of a primary plugin that is kept and exposed as <see cref="PrimaryPluginBase.PrimaryPluginContext"/>
/// and <see cref="PrimaryRepoPlugin{T}.PrimaryPluginContext"/>.
/// <para>
/// This exposes the plugin configuration and per Repo plugin configuration to its plugin, the <see cref="World"/> and
/// the executing <see cref="Command"/>.
/// </para>
/// </summary>
public sealed class PrimaryPluginContext
{
    readonly World _world;
    readonly PluginInfo _pluginInfo;
    readonly PluginConfiguration _configuration;
    Dictionary<Repo,PluginConfiguration>? _perRepoConfigurations;

    /// <summary>
    /// Constructor used by reflection based plugins.
    /// </summary>
    /// <param name="pluginInfo">The plugin information.</param>
    /// <param name="configuration">The plugin configuration.</param>
    /// <param name="world">The world that initializes the plugin.</param>
    public PrimaryPluginContext( PluginInfo pluginInfo, XElement configuration, World world )
    {
        _pluginInfo = pluginInfo;
        _configuration = new PluginConfiguration( this, configuration, null );
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
    /// Gets the command being executed.
    /// <para>
    /// Caution: this is null during plugin initialization. 
    /// </para>
    /// </summary>
    public Command? Command => _world.ExecutingCommand;

    /// <summary>
    /// Gets the plugin info.
    /// </summary>
    public PluginInfo PluginInfo => _pluginInfo;

    /// <summary>
    /// Gets the plugin configuration.
    /// </summary>
    public PluginConfiguration Configuration => _configuration;

    /// <summary>
    /// Gets whether the plugin configuration for a given <see cref="Repo"/> exists.
    /// <para>An empty configuration (when <see cref="PluginConfiguration.IsEmptyConfiguration"/> is true) doesn't exist.</para>
    /// </summary>
    /// <param name="repo">The World's repo for which the per Repo plugin configuration should exist.</param>
    /// <returns>True if th plugin configuration exists, false otherwise.</returns>
    public bool HasConfigurationFor( Repo repo )
    {
        return (_perRepoConfigurations != null
                && _perRepoConfigurations.TryGetValue( repo, out var exists )
                && !exists.IsEmptyConfiguration)
               || repo._configuration.Element( _pluginInfo.GetXName() ) != null;
    }

    /// <summary>
    /// Gets the plugin configuration for a given <see cref="Repo"/> (creates an empty one if needed).
    /// </summary>
    /// <param name="repo">The World's repo for which the per Repo plugin configuration must be obtained.</param>
    /// <returns>The plugin configuration.</returns>
    public PluginConfiguration GetConfigurationFor( Repo repo )
    {
        PluginConfiguration? result;
        if( _perRepoConfigurations != null )
        {
            if( _perRepoConfigurations.TryGetValue( repo, out result ) )
            {
                return result;
            }
        }
        else
        {
            _perRepoConfigurations = new Dictionary<Repo, PluginConfiguration>();
        }
        // e is either anchored (existing) or detached (new). Edit uses this.
        var e = repo._configuration.Element( _pluginInfo.GetXName() )
                ?? new XElement( _pluginInfo.GetXName() );
        result = new PluginConfiguration( this, e, repo );
        _perRepoConfigurations.Add( repo, result );
        return result;
    }

    /// <summary>
    /// Clears and removes the configuration for the provided Repo.
    /// </summary>
    /// <param name="repo">The World's repo for which the per Repo plugin configuration must be removed.</param>
    public void RemoveConfigurationFor( Repo repo )
    {
        if( HasConfigurationFor( repo ) )
        {
            GetConfigurationFor( repo ).ClearRepoConfiguration();
        }
    }

}
