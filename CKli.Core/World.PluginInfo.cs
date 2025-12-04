using CK.Core;
using CSemVer;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CKli.Core;

public sealed partial class World
{
    internal sealed class DisplayInfoPlugin
    {
        public DisplayInfoPlugin( string shortPluginName,
                                  string fullName,
                                  PluginStatus status,
                                  InformationalVersion? version,
                                  XElement? configuration )
        {
            ShortName = shortPluginName;
            FullName = fullName;
            Status = status;
            Version = version;
            Configuration = configuration;
        }
        public string ShortName { get; }
        public string FullName { get; }
        public PluginStatus Status { get; }
        public InformationalVersion? Version { get; }
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
        var disabledPlugin = GetDisabledPluginsHeader();
        if( disabledPlugin != null )
        {
            headerText = disabledPlugin;
            infos = null;
            return true;
        }

        Throw.DebugAssert( "Plugins are not disabled.", _plugins != null );
        var config = _definitionFile.ReadPluginsConfiguration( monitor );
        var loaded = _plugins.Plugins;

        Throw.DebugAssert( "Or we'll not be here.", config != null );

        // No need to optimize this.
        var missings = config.Select( kv => (ShortName: kv.Key.LocalName, FullName: $"CKli.{kv.Key}.Plugin", Configuration: kv.Value.Config) )
                              .Where( e => !loaded.Any( p => p.FullPluginName == e.FullName ) )
                              .Select( e => new DisplayInfoPlugin( e.ShortName, e.FullName, PluginStatus.MissingImplementation, null, e.Configuration ) );
        infos = loaded.Select( p => new DisplayInfoPlugin( p.PluginName, p.FullPluginName, p.Status, p.InformationalVersion, config.GetValueOrDefault( p.PluginName ).Config ) )
                         .Concat( missings )
                         .ToList();

        headerText = $"{loaded.Count} loaded plugins, {config.Count} configured plugins.";
        if( _definitionFile.CompileMode != PluginCompileMode.Release )
        {
            headerText += $" (CompileMode: {_definitionFile.CompileMode})";
        }
        // Even if something fails, we want to display the plugin information.
        return loaded.Count == 0 || _events.SafeRaiseEvent( monitor, new PluginInfoEvent( monitor, this, infos ) );
    }

    internal string? GetDisabledPluginsHeader() => _plugins == null ? "Failed to load Plugins." : null;
}
