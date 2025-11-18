using CK.Core;
using System;

namespace CKli.Core;

/// <summary>
/// Required base class for plugins.
/// <para>
/// Plugins can implement <see cref="IDisposable"/> if needed.
/// Dispose will be called before unloading it.
/// </para>
/// <para>
/// A plugin that specializes this class is "optional": it will be instantiated only if required by
/// another plugin constructor. Use the <see cref="PrimaryPluginBase"/> as the base class for a
/// primary plugin that will be instantiated by default.
/// </para>
/// </summary>
public abstract class PluginBase
{
    readonly World _world;

    /// <summary>
    /// Initializes a support plugin.
    /// Primary plugins must accept a <see cref="PrimaryPluginContext"/> parameter
    /// and call <see cref="PluginBase(PrimaryPluginContext)"/>.
    /// </summary>
    /// <param name="world">The world.</param>
    protected PluginBase( World world )
    {
        _world = world;
    }

    /// <summary>
    /// Gets the world.
    /// </summary>
    protected World World => _world;

    /// <summary>
    /// Optional extension point called once all plugins have been instantiated in the
    /// order of the dependencies. Does nothing by default and returns true. 
    /// <para>
    /// Returning false here is a strong error that prevents the load of the plugins.
    /// </para>
    /// <para>
    /// For <see cref="PrimaryPluginBase"/> (or <see cref="PrimaryRepoPlugin{T}"/>) This can typically be used to
    /// initialize an empty configuration or migrate it.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    internal protected virtual bool Initialize( IActivityMonitor monitor ) => true;
}
