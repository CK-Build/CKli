using CK.Build;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Defines the view of a reference from a project to a package produced by another local solution.
    /// This is a wrapper on <see cref="LocalPackageDependency"/> that helps to resolve the Origin vs. Target
    /// ambiguity.
    /// </summary>
    public readonly struct ImportedLocalPackage
    {
        readonly LocalPackageDependency _dep;

        /// <summary>
        /// Initializes an <see cref="ImportedLocalPackage"/> view.
        /// </summary>
        /// <param name="d">The local package dependency.</param>
        public ImportedLocalPackage( LocalPackageDependency d )
        {
            _dep = d;
        }

        /// <summary>
        /// Gets the solution that produces this package.
        /// </summary>
        public DependentSolution Solution => _dep.Target;

        /// <summary>
        /// Gets the project or solution that imports this package.
        /// </summary>
        public IPackageReferer Importer => _dep.RefererOrigin;

        /// <summary>
        /// Gets the package name and version.
        /// </summary>
        public ArtifactInstance Package => _dep.Reference;
    }
}
