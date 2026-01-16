using CK.Core;
using System;
using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Contains configured plugin instances bound to a <see cref="World"/>.
/// The plugin instances are not exposed to avoid any reference leaks from the plugin
/// assembly load context to the CKli core application and this collection doesn't surface publicly,
/// it is encapsulated by the <see cref="World"/> and the <see cref="PluginMachinery"/>.
/// <para>
/// Only <c>CKli.Plugins.Core</c> implements it.
/// </para>
/// </summary>
public abstract class PluginCollection
{
    object[]? _instantiated;
    IReadOnlyCollection<PluginInfo> _plugins;
    CommandNamespace _commands;

    /// <summary>
    /// Initializes a new <see cref="PluginCollection"/>.
    /// </summary>
    /// <param name="instantiated">The instantiated plugins.</param>
    /// <param name="plugins">The plugin infos.</param>
    /// <param name="commands">The plugin commands.</param>
    protected PluginCollection( object[] instantiated, IReadOnlyCollection<PluginInfo> plugins, CommandNamespace commands )
    {
        _instantiated = instantiated;
        _plugins = plugins;
        _commands = commands;
    }

    /// <summary>
    /// Gets the plugins information.
    /// </summary>
    public IReadOnlyCollection<PluginInfo> Plugins => _plugins;

    /// <summary>
    /// Gets the the commands supported by the plugins.
    /// </summary>
    public CommandNamespace Commands => _commands;

    /// <summary>
    /// Gets whether this collection is empty because plugins failed to load.
    /// </summary>
    public virtual bool HasLoadError => false;

    internal bool CallPluginsInitialization( IActivityMonitor monitor )
    {
        Throw.DebugAssert( "DisposeDisposablePlugins has not been called.", _instantiated != null );
        bool success = true;
        foreach( var o in _instantiated )
        {
            if( o is PluginBase p )
            {
                if( !p.Initialize( monitor ) )
                {
                    monitor.Error( $"Plugin '{p.GetType().FullName}' initialization failed." );
                    success = false;
                }
            }
        }
        return success;
    }

    internal void DisposeDisposablePlugins()
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
}

