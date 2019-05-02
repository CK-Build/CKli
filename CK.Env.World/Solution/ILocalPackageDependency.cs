using CSemVer;

namespace CK.Env
{
    /// <summary>
    /// Captures a dependency description between one project from a <see cref="IDependentSolution"/> (the <see cref="Origin"/>)
    /// to a project produced by another solution (the <see cref="Target"/>).
    /// </summary>
    public interface ILocalPackageDependency
    {
        /// <summary>
        /// Gets the type of the dependency involved (ie. "NuGet", "NPM", etc.).
        /// </summary>
        string Type { get; }

        /// <summary>
        /// Gets the primary solution that references the <see cref="TargetProjectName"/> package.
        /// </summary>
        IDependentSolution Origin { get; }

        /// <summary>
        /// Gets the name of the project that references the <see cref="TargetProjectName"/>.
        /// </summary>
        string OriginProjectName { get; }

        /// <summary>
        /// Gets the version of the reference between <see cref="Origin"/> and <see cref="Target"/>.
        /// </summary>
        SVersion Version { get; }

        /// <summary>
        /// Gets the primary solution that produces this package.
        /// </summary>
        IDependentSolution Target { get; }

        /// <summary>
        /// Gets the target project name that is also the package name.
        /// </summary>
        string TargetProjectName { get; }

    }
}
