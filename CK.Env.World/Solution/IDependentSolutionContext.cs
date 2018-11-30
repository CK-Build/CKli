using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Captures high level dependency information that is enough to orchestrate
    /// global operations at the world level.
    /// </summary>
    public interface IDependentSolutionContext
    {
        /// <summary>
        /// Gets the global, sorted, dependencies informations between solutions.
        /// Note that <see cref="IDependentSolution.Index"/> is the index in this list.
        /// </summary>
        IReadOnlyList<IDependentSolution> Solutions { get; }

        /// <summary>
        /// Gets the package dependencies between solutions.
        /// </summary>
        IReadOnlyCollection<ILocalPackageDependency> PackageDependencies { get; }
    }
}
