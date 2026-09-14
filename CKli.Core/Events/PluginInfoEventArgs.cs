using CK.Core;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Core;

/// <summary>
/// Event raised by "ckli plugin" command to collect plugin information.
/// </summary>
public sealed class PluginInfoEventArgs : WorldEventArgs
{
    readonly List<World.DisplayInfoPlugin> _display;

    internal PluginInfoEventArgs( IActivityMonitor monitor, CKliEnv context, World world, List<World.DisplayInfoPlugin> display )
        : base( monitor, context, world )
    {
        _display = display;
    }

    /// <summary>
    /// Adds a message to the plugin information.
    /// <para>
    /// A plugin can call this more than once (a plugin that supports more than one attribute typically
    /// describes each of them): the messages are stacked, they don't replace each other.
    /// </para>
    /// </summary>
    /// <param name="source">The plugin that emitted the message.</param>
    /// <param name="message">A renderable message.</param>
    public void AddMessage( PrimaryPluginContext source, IRenderable message )
    {
        var p = _display.FirstOrDefault( d => d.FullName == source.PluginInfo.FullPluginName );
        if( p == null )
        {
            Monitor.Warn( $"""
                Unable to locate source named '{source.PluginInfo.FullPluginName}' in loaded plugins.
                PluginInfoEvent message:
                {message.RenderAsString()}
                is not collected.
                """ );
            return;
        }
        p.Message = p.Message == null ? message : p.Message.AddBelow( message );
    }
}
