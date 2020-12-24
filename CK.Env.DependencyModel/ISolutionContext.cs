using CK.Core;
using System.Collections.Generic;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Solution context handles a set of solutions and their projects.
    /// The centralized <see cref="UpdateSerialNumber"/> enables any derived information
    /// from these solutions to handle synchronization.
    /// </summary>
    public interface ISolutionContext : IReadOnlyCollection<ISolution>
    {
        /// <summary>
        /// Gets a <see cref="DependencyAnalyzer"/> that is up to date (based on the <see cref="UpdateSerialNumber"/>).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="traceGraphDetails">True to trace the details of the input and output (sorted) graphs.</param>
        /// <returns>The up to date dependency context.</returns>
        DependencyAnalyzer GetDependencyAnalyser( IActivityMonitor m, bool traceGraphDetails );

        /// <summary>
        /// Gets the current update serial number. This changes each time
        /// anything changes in the solutions or projects.
        /// </summary>
        int UpdateSerialNumber { get; }

    }
}
