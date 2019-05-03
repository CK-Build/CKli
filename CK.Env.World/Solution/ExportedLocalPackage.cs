namespace CK.Env
{
    /// <summary>
    /// Defines the view of a locally produced package exported by a solution into
    /// the other solutions. 
    /// This is a wrapper on <see cref="ILocalPackageDependency"/> that helps to resolve the Origin vs. Target
    /// ambiguity.
    /// </summary>
    public readonly struct ExportedLocalPackage
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
        /// Gets the type of the dependency involved (ie. "NuGet", "NPM", etc.).
        /// </summary>
        public ArtifactType Type => _dep.Type;

        /// <summary>
        /// Gets the primary solution that consumes this package.
        /// </summary>
        public IDependentSolution Target => _dep.Origin;

        /// <summary>
        /// Gets the name that reference this package.
        /// </summary>
        public string TargetProjectName => _dep.OriginProjectName;

        /// <summary>
        /// Gets the package name.
        /// </summary>
        public string PackageName => _dep.TargetProjectName;
    }
}
