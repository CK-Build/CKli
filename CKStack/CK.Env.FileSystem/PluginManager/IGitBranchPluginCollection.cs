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
        /// Gets whether the plugins are initialized for the specified branch or
        /// if <see cref="EnsurePlugins(IActivityMonitor, string)"/> must be called.
        /// </summary>
        /// <param name="branchName">The branch name.</param>
        /// <returns>Whether the collection has been initialized.</returns>
        bool IsInitialized( string branchName );

        /// <summary>
        /// Gets the plugins for a specified branch. The collection is created as needed but if an error occurred
        /// (typically during plugin instantiation), this throws the error.
        /// Use <see cref="EnsurePlugins(IActivityMonitor, string, string)"/> to load the plugins in a safer way.
        /// </summary>
        /// <param name="branchName">The branch name. Cannot be null or whitespace.</param>
        /// <returns>The branch plugin collection for the specified branch.</returns>
        IGitPluginCollection<IGitBranchPlugin> this[string branchName] { get; }

        /// <summary>
        /// Ensures that the collection is created and plugins are instantiated.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The branch name. Cannot be null or whitespace.</param>
        /// <returns>True on success, false on error.</returns>
        bool EnsurePlugins( IActivityMonitor m, string branchName );
    }
}
