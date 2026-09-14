using CK.Core;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Base class for primary <see cref="RepoPluginBase{T}"/>: these plugins are always instantiated and have access to their
/// configurations, the <see cref="World"/> and the executing <see cref="Command"/> through the <see cref="PrimaryPluginContext"/>.
/// </summary>
/// <typeparam name="T">The information type.</typeparam>
public abstract class PrimaryRepoPlugin<T> : RepoPluginBase<T>, IPrimaryPlugin
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

    PluginInfo IPrimaryPlugin.PluginInfo => _primaryContext.PluginInfo;

    Task<bool?> IPrimaryPlugin.OnPluginSetAsync( IActivityMonitor monitor, PluginInfo? pluginInfo, string attributeName, string? attributeValue ) => OnPluginSetAsync( monitor, pluginInfo, attributeName, attributeValue );

    /// <summary>
    /// Handles "ckli plugin set" command.
    /// The first true Error or Handled returned value stops the submission to the subsequent plugins.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="pluginInfo">The plugin information if it has been provided (as the first part of the identifier): it is this <see cref="PrimaryPluginContext.PluginInfo"/> or it is null.</param>
    /// <param name="attributeName">The attribute name to set.</param>
    /// <param name="attributeValue">The attribute value to set or null to unset it.</param>
    /// <returns>
    /// Whether an error occurred (false) or the <paramref name="attributeName"/> has been successfully handled by this plugin (true), or this attribute
    /// is not handled by this plugin. 
    /// </returns>
    protected virtual Task<bool?> OnPluginSetAsync( IActivityMonitor monitor, PluginInfo? pluginInfo, string attributeName, string? attributeValue ) => Task.FromResult<bool?>( null );


}
