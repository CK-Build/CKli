using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Exposes the <see cref="IGitBranchPlugin"/> collection for each branch.
    /// </summary>
    public interface IGitBranchPluginCollection : IReadOnlyCollection<IGitPluginCollection<IGitBranchPlugin>>
    {
        /// <summary>
        /// Gets the plugins for a specified branch. The collection is created as needed: this is the same
        /// as calling <see cref="GetPlugins"/>.
        /// </summary>
        /// <param name="branchName">The branch name. Cannot be null or whitespace.</param>
        /// <returns>The branch plugin collection for the specified branch.</returns>
        IGitPluginCollection<IGitBranchPlugin> this[string branchName] { get; }

        /// <summary>
        /// Gets the plugins for a specified branch. The collection is created as needed.
        /// </summary>
        /// <param name="branchName">The branch name. Cannot be null or whitespace.</param>
        /// <returns>The branch plugin collection for the specified branch.</returns>
        IGitPluginCollection<IGitBranchPlugin> GetPlugins( string branchName );

    }
}
