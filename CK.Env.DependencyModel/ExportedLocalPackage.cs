namespace CK.Env
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
        /// Gets the primary solution that consumes this package.
        /// </summary>
        public DependentSolution Target => _dep.Origin;

        /// <summary>
        /// Gets the project that references this package.
        /// </summary>
        public DependentProject TargetProject => _dep.OriginProject;

        /// <summary>
        /// Gets the type of the dependency involved (ie. "NuGet", "NPM", etc.).
        /// </summary>
        public ArtifactInstance Reference => _dep.Reference;

    }
}
