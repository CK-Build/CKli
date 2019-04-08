namespace CK.Env
{
    /// <summary>
    /// Defines the view of a reference from a project to a package produced by another local solution.
    /// This is a wrapper on <see cref="ILocalPackageDependency"/> that helps to resolve the Origin vs. Target
    /// ambiguity.
    /// </summary>
    public readonly struct ImportedLocalPackage
    {
        readonly ILocalPackageDependency _dep;

        /// <summary>
        /// Initializes an <see cref="ImportedLocalPackage"/> view.
        /// </summary>
        /// <param name="d">The local package dependency.</param>
        public ImportedLocalPackage( ILocalPackageDependency d )
        {
            _dep = d;
            Package = new ArtifactInstance( d.Type, d.TargetProjectName, d.Version );
        }

        /// <summary>
        /// Gets the primary solution that produces this package.
        /// </summary>
        public IDependentSolution Solution => _dep.Target;

        /// <summary>
        /// Gets the secondary solution name of the solution that actually produces this package
        /// or null if it is the primary <see cref="Solution"/> itself.
        /// </summary>
        public string SecondarySolutionName => _dep.TargetSecondarySolutionName;

        /// <summary>
        /// Gets the project name that references the <see cref="Package"/>.
        /// </summary>
        public string ProjectName => _dep.OriginProjectName;

        /// <summary>
        /// Gets the package name and version.
        /// </summary>
        public ArtifactInstance Package { get; }
    }
}
