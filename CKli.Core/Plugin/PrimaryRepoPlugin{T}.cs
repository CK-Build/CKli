namespace CKli.Core;

/// <summary>
/// Base class for primary <see cref="RepoPluginBase{T}"/>: these plugins are always instantiated and have access to the
/// Xml configuration element through the <see cref="PrimaryPluginContext"/>.
/// </summary>
/// <typeparam name="T">The information type.</typeparam>
public abstract class PrimaryRepoPlugin<T> : RepoPluginBase<T>
    where T : RepoInfo
{
    readonly PrimaryPluginContext _primaryContext;

    /// <summary>
    /// Initializes a primary plugin.
    /// </summary>
    /// <param name="primaryContext">The primary plugin context.</param>
    protected PrimaryRepoPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext.World )
    {
        _primaryContext = primaryContext;
    }

    /// <summary>
    /// Gets the context of this primary plugin.
    /// </summary>
    protected PrimaryPluginContext PrimaryPluginContext => _primaryContext;

}
