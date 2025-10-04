using System;
using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Contains configured plugins instances bound to a world.
/// This only dispatches the Dispose call to any IDisposable objects.
/// </summary>
public sealed class PluginCollection : IPluginCollection
{
    object[]? _instantiated;
    IReadOnlyCollection<PluginInfo> _plugins;
    CommandNamespace _commands;

    PluginCollection( object[] instantiated, IReadOnlyCollection<PluginInfo> plugins, CommandNamespace commands )
    {
        _instantiated = instantiated;
        _plugins = plugins;
        _commands = commands;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PluginInfo> Plugins => _plugins;

    /// <inheritdoc />
    public CommandNamespace Commands => _commands;

    void IDisposable.Dispose()
    {
        if( _instantiated != null )
        {
            // Commands (that holds the types and instances) are cleared
            // by the World.
            foreach( var o in _instantiated )
            {
                if( o is IDisposable d ) d.Dispose();
            }
            _instantiated = null;
        }
    }

    /// <summary>
    /// Instantiates a ready-to-use PluginCollection.
    /// This supports the plugin infrastructure and is not intended to be called directly.
    /// </summary>
    /// <param name="instantiated">The plugin instances.</param>
    /// <param name="plugins">The plugin informations.</param>
    /// <param name="commands">The commands.</param>
    /// <param name="pluginCommands">The plugin commands to bind.</param>
    public static PluginCollection CreateAndBindCommands( object[] instantiated,
                                                          IReadOnlyCollection<PluginInfo> plugins,
                                                          CommandNamespace commands,
                                                          IEnumerable<PluginCommand> pluginCommands )
    {
        foreach( var c in pluginCommands )
        {
            int idx = c.PluginTypeInfo.ActivationIndex;
            if( idx >= 0 )
            {
                c._instance = instantiated[idx];
            }
        }
        return new PluginCollection( instantiated, plugins, commands );
    }
}


