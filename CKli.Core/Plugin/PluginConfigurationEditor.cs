using CK.Core;
using System;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Allows primary Plugins to edit their configuration.
/// This can be obtained from the <see cref="PrimaryPluginContext.XmlConfigurationEditor"/>.
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
    /// This can fail and return false if an exception is thrown by <paramref name="editor"/>
    /// or if the <see cref="WorldDefinitionFile"/> cannot be saved or commit in the <see cref="StackRepository"/> fails.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="editor">The mutation to apply.</param>
    /// <returns>True on success, false otherwise.</returns>
    public bool Edit( IActivityMonitor monitor, Action<IActivityMonitor,XElement> editor )
    {
        using( _definitionFile.StartEdit( _xmlConfiguration ) )
        {
            try
            {
                editor( monitor, _xmlConfiguration );
            }
            catch( Exception ex )
            {
                monitor.Error( $"""
                    Plugin '{_pluginInfo.PluginName}' error while editing configuration:
                    {_xmlConfiguration}
                    """, ex );
                return false;
            }
        }
        return _definitionFile.SaveFile( monitor )
               && _definitionFile.World.Stack.Commit( monitor, $"Plugin '{_pluginInfo.PluginName}' changed its configuration." );
    }

}
