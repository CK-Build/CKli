using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Contains configured plugins instances bound to a world.
/// This only dispatches the Dispose call to any IDisposable objects.
/// </summary>
public sealed class PluginCollectionImpl : PluginCollection
{
    PluginCollectionImpl( object[] instantiated, IReadOnlyCollection<PluginInfo> plugins, CommandNamespace commands )
        : base( instantiated, plugins, commands )
    {
    }

    /// <summary>
    /// Instantiates a ready-to-use PluginCollection.
    /// This supports the plugin infrastructure and is not intended to be called directly.
    /// </summary>
    /// <param name="instantiated">The plugin instances.</param>
    /// <param name="plugins">The plugin information.</param>
    /// <param name="commands">The commands.</param>
    /// <param name="pluginCommands">The plugin commands to bind.</param>
    public static PluginCollectionImpl CreateAndBindCommands( object[] instantiated,
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
        return new PluginCollectionImpl( instantiated, plugins, commands );
    }
}


