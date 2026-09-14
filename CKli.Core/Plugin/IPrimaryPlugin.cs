using CK.Core;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Unifies <see cref="PrimaryPluginBase"/> and <see cref="PrimaryRepoPlugin{T}"/>.
/// </summary>
internal interface IPrimaryPlugin
{
    /// <summary>
    /// Gets the plugin information: this is the <see cref="PrimaryPluginContext.PluginInfo"/>.
    /// </summary>
    PluginInfo PluginInfo { get; }

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
    Task<bool?> OnPluginSetAsync( IActivityMonitor monitor, PluginInfo? pluginInfo, string attributeName, string? attributeValue );
}
