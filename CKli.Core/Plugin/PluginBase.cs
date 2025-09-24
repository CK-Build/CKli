using System;

namespace CKli.Core;

/// <summary>
/// Required base class for plugins.
/// Plugins can implement <see cref="IDisposable"/> if needed.
/// Dispose will be called before unloading it.
/// </summary>
public abstract class PluginBase
{
    readonly World _world;
    readonly IPrimaryPluginContext? _primaryContext;

    /// <summary>
    /// Initializes this plugin on its World.
    /// This plugin may be a primary plugin but primary plugins are typically initialized
    /// with their context by <see cref="PluginBase(IPrimaryPluginContext)"/>
    /// </summary>
    /// <param name="world">The world.</param>
    protected PluginBase( World world )
    {
        _world = world;
    }

    /// <summary>
    /// Initializes a primary plugin.
    /// </summary>
    /// <param name="primaryContext">The primary plugin context.</param>
    protected PluginBase( IPrimaryPluginContext primaryContext )
        : this( primaryContext.World )
    {
        _primaryContext = primaryContext;
    }

    /// <summary>
    /// Gets the world.
    /// </summary>
    public World World => _world;

    /// <summary>
    /// Gets the context of this plugin if it has been initialized by <see cref="PluginBase(IPrimaryPluginContext)"/>.
    /// </summary>
    public IPrimaryPluginContext? PrimaryContext => _primaryContext;
}
