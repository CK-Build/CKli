using CK.Core;
using System;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Allows primary Plugins to edit their configuration.
/// This can be obtained from the <see cref="PrimaryPluginContext.ConfigurationEditor"/>.
/// </summary>
public sealed class PluginConfigurationEditor
{
    readonly PluginInfo _pluginInfo;
    readonly WorldDefinitionFile _definitionFile;
    readonly XElement _xmlConfiguration;

    internal PluginConfigurationEditor( PluginInfo pluginInfo, WorldDefinitionFile definitionFile, XElement xmlConfiguration )
    {
        _pluginInfo = pluginInfo;
        _definitionFile = definitionFile;
        _xmlConfiguration = xmlConfiguration;
    }

    /// <summary>
    /// Allows the plugin configuration to be changed.
    /// <para>
    /// This can fail and return false if an exception is thrown by <paramref name="editor"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="editor">The mutation to apply.</param>
    /// <returns>True on success, false otherwise.</returns>
    public bool EditConfiguration( IActivityMonitor monitor, Action<IActivityMonitor,PluginConfiguration> editor )
    {
        try
        {
            var clone = new XElement( _xmlConfiguration );
            editor( monitor, new PluginConfiguration( clone ) );
            using( _definitionFile.StartEdit() )
            {
                _xmlConfiguration.RemoveAll();
                _xmlConfiguration.Add( clone.Attributes() );
                _xmlConfiguration.Add( clone.Nodes() );
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"""
                Plugin '{_pluginInfo.PluginName}' error while editing configuration:
                {_xmlConfiguration}
                """, ex );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Ensures that a per Repo plugin configuration exists and allows changing it.
    /// <para>
    /// This can fail and return false if an exception is thrown by <paramref name="editor"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">
    /// The World's repo for which the per Repo plugin configuration must be created if missing and changed.
    /// </param>
    /// <param name="editor">The mutation to apply to the per Repo plugin configuration.</param>
    /// <returns>True on success, false otherwise.</returns>
    public bool EditConfigurationFor( IActivityMonitor monitor,
                                      Repo repo,
                                      Action<IActivityMonitor, PluginConfiguration> editor )
    {
        var originalElement = repo._configuration.Element( _pluginInfo.GetXName() );
        var e = originalElement != null
                    ? new XElement( originalElement )
                    : new XElement( _pluginInfo.GetXName() );
        try
        {
            editor( monitor, new PluginConfiguration( e ) );
            using( _definitionFile.StartEdit() )
            {
                if( originalElement == null )
                {
                    // This may be changed by the editor (corrects that).
                    e.Name = _pluginInfo.GetXName();
                    repo._configuration.Add( e );
                }
                else
                {
                    originalElement.RemoveAll();
                    originalElement.Add( e.Attributes() );
                    originalElement.Add( e.Nodes() );
                }
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"""
                Plugin '{_pluginInfo.PluginName}' error while editing configuration for '{repo.DisplayPath}':
                {e}
                """, ex );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Deletes a per Repo plugin configuration or does nothing if it doesn't exist.
    /// </summary>
    /// <param name="repo">
    /// The World's repo for which the per Repo plugin configuration must be deleted.
    /// </param>
    /// <returns>True if the configuration element has been removed, false if it doesn't exist.</returns>
    public bool DeleteConfigurationFor( Repo repo )
    {
        var e = repo._configuration.Element( _pluginInfo.GetXName() );
        if( e != null )
        {
            using( _definitionFile.StartEdit() )
            {
                e.Remove();
            }
            return true;
        }
        return false;
    }

}
