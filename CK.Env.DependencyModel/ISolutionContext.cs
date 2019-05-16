using CK.Core;
using System.Collections.Generic;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Solution context handles a set of solutions and their projects.
    /// The centralized <see cref="Version"/> enables any derived information
    /// from these solutions to handle synchronization.
    /// </summary>
    public interface ISolutionContext : IReadOnlyCollection<ISolution>
    {
        /// <summary>
        /// Gets a <see cref="DependencyAnalyzer"/> that is up to date (based on the <see cref="Version"/>).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The up to date dependency context.</returns>
        DependencyAnalyzer GetDependencyAnalyser( IActivityMonitor m );

        /// <summary>
        /// Gets the current version. This changes each time
        /// anything changes in the solutions or projects.
        /// </summary>
        int Version { get; }

    }
}
