using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Defines the view of a locally produced package exported by a solution into
    /// other solutions. 
    /// </summary>
    public struct ExportedLocalPackage
    {
        readonly ILocalPackageDependency _dep;

        /// <summary>
        /// Initializes an <see cref="ExportedLocalPackage"/> view.
        /// </summary>
        /// <param name="d">The local package dependency.</param>
        public ExportedLocalPackage( ILocalPackageDependency d )
        {
            _dep = d;
        }

        /// <summary>
        /// Gets the primary solution that consumes this package.
        /// </summary>
        public IDependentSolution Target => _dep.Origin;

        /// <summary>
        /// Gets the secondary solution name of the solution that actually consumes this package
        /// or null if it is the <see cref="Target"/> primary solution that consumes it.
        /// </summary>
        public string TargetSecondarySolutionName => _dep.OriginSecondarySolutionName;

        /// <summary>
        /// Gets the package name.
        /// </summary>
        public string TargetProjectName => _dep.TargetProjectName;
    }
}
