using CK.Build;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Defines the view of a locally produced package exported by a solution into
    /// the other solutions. 
    /// This is a wrapper on <see cref="LocalPackageDependency"/> that helps to resolve the Origin vs. Target
    /// ambiguity.
    /// </summary>
    public readonly struct ExportedLocalPackage
    {
        readonly LocalPackageDependency _dep;

        /// <summary>
        /// Initializes an <see cref="ExportedLocalPackage"/> view.
        /// </summary>
        /// <param name="d">The local package dependency.</param>
        public ExportedLocalPackage( LocalPackageDependency d )
        {
            _dep = d;
        }

        /// <summary>
        /// Gets the solution that consumes this package.
        /// </summary>
        public DependentSolution Solution => _dep.Origin;

        /// <summary>
        /// Gets the project or solution that generates this package.
        /// </summary>
        public IPackageReferrer Exporter => _dep.TargetProject;

        /// <summary>
        /// Gets the package name and version.
        /// </summary>
        public ArtifactInstance Package => _dep.Reference;

    }
}
