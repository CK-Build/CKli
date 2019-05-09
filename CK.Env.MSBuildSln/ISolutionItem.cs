using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// Enables sections to have access to the <see cref="SolutionFile"/>.
    /// </summary>
    public interface ISolutionItem
    {
        /// <summary>
        /// Gets root the solution.
        /// </summary>
        SolutionFile Solution { get; }
    }
}
