using CK.Core;
using CK.Env.DependencyModel;
using CSemVer;

namespace CK.Env
{
    /// <summary>
    /// Captures basic information that describes a package update for a project.
    /// </summary>
    public class UpdatePackageInfo
    {
        /// <summary>
        /// Initializes a new <see cref="UpdatePackageInfo"/>.
        /// </summary>
        /// <param name="project">The project. Can not be null.</param>
        /// <param name="package">The package identifier and version to upgrade.</param>
        public UpdatePackageInfo( IProject project, ArtifactInstance package )
        {
            Project = project;
            PackageUpdate = package;
        }

        /// <summary>
        /// Initializes a new <see cref="UpdatePackageInfo"/>.
        /// </summary>
        /// <param name="project">The project. Can not be null.</param>
        /// <param name="packageType">Type of the package.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The package version.</param>
        public UpdatePackageInfo( IProject project, ArtifactType packageType, string packageId, SVersion version )
            : this( project, new ArtifactInstance( packageType, packageId, version ) )
        {
        }

        /// <summary>
        /// Gets the project that must be updated.
        /// </summary>
        public IProject Project { get; }

        /// <summary>
        /// Gets the package to update and its target version.
        /// </summary>
        public ArtifactInstance PackageUpdate { get; }

        /// <summary>
        /// Overridden to return $"{Project.Name} <= {PackageUpdate}".
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"{Project.Name} <= {PackageUpdate}";

    }
}
