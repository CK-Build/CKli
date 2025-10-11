using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli.Core;

public sealed partial class World
{
    internal sealed class DisplayInfoPlugin
    {
        public DisplayInfoPlugin( string shortPluginName, string fullName, PluginStatus status, XElement? configuration )
        {
            ShortName = shortPluginName;
            FullName = fullName;
            Status = status;
            Configuration = configuration;
        }
        public string ShortName { get; }
        public string FullName { get; }
        public PluginStatus Status { get; }
        public XElement? Configuration { get; }
        public IRenderable? Message { get; set; }
    }

    /// <summary>
    /// Collects plugin information produces a textual information to be displayed.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="headerText">The text to display (whether the plugins are disabled or not).</param>
    /// <param name="infos">The plugin infos, null when the plugins are disabled.</param>
    /// <returns>True on success, false on error.</returns>
    internal bool RaisePluginInfo( IActivityMonitor monitor, out string headerText, out List<DisplayInfoPlugin>? infos )
    {
        var definitionFile = DefinitionFile;
        if( definitionFile.IsPluginsDisabled )
        {
            infos = null;
            headerText = $"""
                    Plugins are disabled by configuration.
                    Configurations:
                    {definitionFile.Plugins}
                    """;
            return true;
        }
        if( _plugins == null )
        {
            infos = null;
            headerText = "No configured PluginLoader. Plugins are disabled.";
            return true;
        }
        var config = definitionFile.ReadPluginsConfiguration( monitor );
        var loaded = _plugins.Plugins;

        Throw.DebugAssert( "Or we'll not be here.", config != null );

        // No need to optimize this.
        var missings = config.Select( kv => (ShortName: kv.Key, FullName: $"CKli.{kv.Key}.Plugin", Configuration: kv.Value.Config) )
                              .Where( e => !loaded.Any( p => p.FullPluginName == e.FullName ) )
                              .Select( e => new DisplayInfoPlugin( e.ShortName, e.FullName, PluginStatus.MissingImplementation, e.Configuration ) );
        infos = loaded.Select( p => new DisplayInfoPlugin( p.PluginName, p.FullPluginName, p.Status, config.GetValueOrDefault( p.PluginName ).Config ) )
                         .Concat( missings )
                         .ToList();

        headerText = $"{loaded.Count} loaded plugins, {config.Count} configured plugins.";
        // Even if something fails, we want to display the plugin information.
        return loaded.Count == 0 || _events.SafeRaiseEvent( monitor, new PluginInfoEvent( monitor, this, infos ) );
    }

}
