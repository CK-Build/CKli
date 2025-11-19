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
    /// Allows the Xml configuration to be changed.
    /// <para>
    /// This can fail and return false if an exception is thrown by <paramref name="editor"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="editor">The mutation to apply.</param>
    /// <returns>True on success, false otherwise.</returns>
    public bool Edit( IActivityMonitor monitor, Action<IActivityMonitor,XElement> editor )
    {
        try
        {
            var clone = new XElement( _xmlConfiguration );
            editor( monitor, clone );
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

}
