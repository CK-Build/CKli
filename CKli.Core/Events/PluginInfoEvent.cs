using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CKli.Core;

public sealed class PluginInfoEvent : WorldEvent
{
    readonly List<World.DisplayInfoPlugin> _display;

    internal PluginInfoEvent( IActivityMonitor monitor, World world, List<World.DisplayInfoPlugin> display )
        : base( monitor, world )
    {
        _display = display;
    }

    /// <summary>
    /// Adds a message to the plugin information.
    /// </summary>
    /// <param name="source">The plugin that emitted the message.</param>
    /// <param name="message">A message builder.</param>
    public void AddMessage( PrimaryPluginContext source, Action<StringBuilder> message )
    {
        var p = _display.FirstOrDefault( d => d.FullName == source.PluginInfo.FullPluginName );
        if( p == null )
        {
            Monitor.Warn( $"""
                Unable to locate source named '{source.PluginInfo.FullPluginName}' in loaded plugins.
                PluginInfoEvent message:
                {message}
                is not collected.
                """ );
            return;
        }
        p.Add( message );
    }
}
