using CK.Core;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Exposes the <see cref="IGitBranchPlugin"/> collection for each branch.
    /// </summary>
    public interface IGitBranchPluginCollection : IReadOnlyCollection<IGitPluginCollection<IGitBranchPlugin>>
    {
        /// <summary>
        /// Gets the plugins for a specified branch. The collection is created as needed but if an error occurred
        /// (typically during plugin instanciation), this throws the error.
        /// Use <see cref="EnsurePlugins(IActivityMonitor, string, string)"/> to load the plugins in a safer way.
        /// </summary>
        /// <param name="branchName">The branch name. Cannot be null or whitespace.</param>
        /// <returns>The branch plugin collection for the specified branch.</returns>
        IGitPluginCollection<IGitBranchPlugin> this[string branchName] { get; }

        /// <summary>
        /// Ensures that the collection is created and plugins are instanciated.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The branch name. Cannot be null or whitespace.</param>
        /// <param name="holderName">The name of the plugin container used for logging.</param>
        /// <returns>True on success, false on error.</returns>
        bool EnsurePlugins( IActivityMonitor m, string branchName, string holderName );

    }
}
