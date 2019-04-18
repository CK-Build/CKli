using CSemVer;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Describes a dependent NPM package in a <see cref="DependencyAnalyser"/> context.
    /// A dependent package may be an external or local package (a local published <see cref="Project"/>
    /// exists that produces this package).
    /// </summary>
    public interface INPMDependentPackage : IDependentPackage
    {
        /// <summary>
        /// Gets the local project that produces this package or null for external packages.
        /// </summary>
        NPMProject Project { get; }
    }
}
