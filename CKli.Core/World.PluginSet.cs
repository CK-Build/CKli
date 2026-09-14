using CK.Core;
using System;
using System.Threading.Tasks;

namespace CKli.Core;

public sealed partial class World
{
    /// <summary>
    /// Submits a plugin attribute name and its value (or null to unset it) to the primary plugins until
    /// one of them handles it: this is the "ckli plugin set" and "ckli plugin unset" commands.
    /// <para>
    /// The <paramref name="identifier"/> is an attribute name ("RemoveUselessFakeTag") or a plugin short name
    /// and an attribute name ("VersionTag.RemoveUselessFakeTag"). The long form is required only when more
    /// than one plugin handles the same attribute name: the first plugin that handles it wins.
    /// </para>
    /// <para>
    /// On success the <see cref="DefinitionFile"/> is dirty: it is saved and committed by <see cref="StackRepository.Close(IActivityMonitor)"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="identifier">The attribute name, optionally prefixed by the plugin short name and a dot.</param>
    /// <param name="attributeValue">The value to set or null to unset the attribute.</param>
    /// <returns>True on success, false on error.</returns>
    internal async Task<bool> PluginSetAsync( IActivityMonitor monitor, string identifier, string? attributeValue )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( identifier );
        var disabledPlugin = GetDisabledPluginsHeader();
        if( disabledPlugin != null )
        {
            monitor.Error( $"""
                {disabledPlugin}
                Unable to configure a plugin attribute.
                """ );
            return false;
        }
        Throw.DebugAssert( "Plugins are not disabled.", _plugins != null );

        string? pluginName = null;
        string attributeName = identifier;
        int idx = identifier.IndexOf( '.' );
        if( idx >= 0 )
        {
            pluginName = identifier.Substring( 0, idx );
            attributeName = identifier.Substring( idx + 1 );
            if( pluginName.Length == 0 || attributeName.Length == 0 || attributeName.Contains( '.' ) )
            {
                monitor.Error( $"""
                    Invalid plugin attribute identifier '{identifier}'.
                    It must be an attribute name ("RemoveUselessFakeTag") or a plugin short name and an attribute name ("VersionTag.RemoveUselessFakeTag").
                    """ );
                return false;
            }
        }
        bool pluginFound = false;
        // The first plugin that answers a non null result wins: subsequent plugins are not solicited.
        foreach( var p in _plugins.GetPrimaryPlugins() )
        {
            var info = p.PluginInfo;
            if( pluginName != null && !info.PluginName.Equals( pluginName, StringComparison.OrdinalIgnoreCase ) )
            {
                continue;
            }
            pluginFound = true;
            var handled = await p.OnPluginSetAsync( monitor, pluginName != null ? info : null, attributeName, attributeValue )
                                 .ConfigureAwait( false );
            if( handled.HasValue )
            {
                if( !handled.Value ) return false;
                monitor.Info( ScreenType.CKliScreenTag,
                              attributeValue != null
                                ? $"""Plugin '{info.PluginName}': {attributeName} set to "{attributeValue}"."""
                                : $"Plugin '{info.PluginName}': {attributeName} unset." );
                return true;
            }
        }
        if( pluginName != null && !pluginFound )
        {
            monitor.Error( $"""
                Plugin '{pluginName}' not found: it is not an enabled primary plugin of this World.
                Use "ckli plugin info" to list the plugins and the attributes they support.
                """ );
        }
        else
        {
            monitor.Error( $"""
                Attribute '{attributeName}' is not supported{(pluginName != null ? $" by the '{pluginName}' plugin" : " by any plugin of this World")}.
                Use "ckli plugin info" to list the plugins and the attributes they support.
                """ );
        }
        return false;
    }
}
