namespace CKli.Core;

/// <summary>
/// Base class for primary plugins: these plugins are always instantiated and have access to the
/// Xml configuration element through the <see cref="PrimaryPluginContext"/>.
/// </summary>
public abstract class PrimaryPluginBase : PluginBase
{
    readonly PrimaryPluginContext _primaryContext;

    /// <summary>
    /// Initializes a primary plugin.
    /// </summary>
    /// <param name="primaryContext">The primary plugin context.</param>
    protected PrimaryPluginBase( PrimaryPluginContext primaryContext )
        : base( primaryContext.World )
    {
        _primaryContext = primaryContext;
    }

    /// <summary>
    /// Gets the context of this primary plugin.
    /// </summary>
    protected PrimaryPluginContext PrimaryPluginContext => _primaryContext;
}
