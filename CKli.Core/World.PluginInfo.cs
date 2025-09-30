using CK.Core;
using System;
using System.Linq;
using System.Text;

namespace CKli.Core;

public sealed partial class World
{
    internal sealed class DisplayInfoPlugin
    {
        StringBuilder? _message;

        public DisplayInfoPlugin( string shortPluginName, string fullName, PluginStatus status )
        {
            ShortName = shortPluginName;
            FullName = fullName;
            Status = status;
        }
        public string ShortName { get; }
        public string FullName { get; }
        public PluginStatus Status { get; }

        public void Add( Action<StringBuilder> message )
        {
            _message ??= new StringBuilder();
            message( _message );
        }

        public string? Message => _message?.ToString();
    }

    /// <summary>
    /// Collects plugin information and outputs a textual information.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="text">The output text.</param>
    /// <returns>True on success, false on error.</returns>
    public bool RaisePluginInfo( IActivityMonitor monitor, out string text )
    {
        var definitionFile = DefinitionFile;
        if( definitionFile.IsPluginsDisabled )
        {
            text = $"""
                    Plugins are disabled by configuration.
                    Configurations:
                    {definitionFile.Plugins}
                    """;
            return true;
        }
        if( _pluginMachinery == null )
        {
            text = $"No configured PluginLoader. Plugins are disabled.";
            return true;
        }
        var config = definitionFile.ReadPluginsConfiguration( monitor );
        var loaded = _pluginMachinery.WorldPlugins.Plugins;

        Throw.DebugAssert( "Or we'll not be here.", config != null );

        // No need to optimize this.
        var missings = config.Keys.Select( k => (ShortName: k, FullName: $"CKli.{k}.Plugin") )
                                  .Where( e => !loaded.Any( p => p.FullPluginName == e.FullName ) )
                                  .Select( e => new DisplayInfoPlugin( e.ShortName, e.FullName, PluginStatus.MissingImplementation ) );
        var list = loaded.Select( p => new DisplayInfoPlugin( p.PluginName, p.FullPluginName, p.Status ) )
                         .Concat( missings )
                         .ToList();

        // Even if something fails, we want to display the plugin information.
        bool success = loaded.Count == 0
                            ? true
                            : _events.SafeRaiseEvent( monitor, new PluginInfoEvent( monitor, this, list ) );
        var b = new StringBuilder();
        b.Append( $"{loaded.Count} loaded plugins, {config.Count} configured plugins." ).AppendLine();
        foreach( var i in list )
        {
            b.Append( $"{i.ShortName}, {i.Status.GetTextStatus()}, {i.Message}" ).AppendLine();
        }
        text = b.ToString();
        return success;
    }

}
