using CK.Core;

using System.Collections.Generic;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Generalizes <see cref="ISolution"/> and <see cref="IProject"/>.
    /// </summary>
    public interface IPackageReferer
    {
        /// <summary>
        /// Gets the solution that is either this referer or the project's solution.
        /// </summary>
        ISolution Solution { get; }

        /// <summary>
        /// Gets the name. This uniquely identify this solution or project. 
        /// </summary>
        string Name { get; }
    }
}
