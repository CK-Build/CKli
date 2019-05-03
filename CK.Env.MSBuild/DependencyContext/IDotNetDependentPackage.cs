using CSemVer;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Describes a dependent package in a <see cref="DependencyAnalyser"/> context.
    /// A dependent package may be an external or local package (a local published <see cref="Project"/>
    /// exists that produces this package).
    /// </summary>
    public interface IDotNetDependentPackage : IDependentPackage
    {
        /// <summary>
        /// Gets the local project that produces this package or null for external packages.
        /// When a local project exists, this <see cref="FullName"/> is the <see cref="ProjectBase.Name"/>
        /// and this <see cref="Version"/> is null.
        /// </summary>
        Project Project { get; }
    }
}
