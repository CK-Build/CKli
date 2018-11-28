using CSemVer;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Describes a dependent package  in a <see cref="DependencyAnalyser"/> context.
    /// A dependent package may be an external or local package (a local published <see cref="Project"/> exists that
    /// produces this package).
    /// </summary>
    public interface IDependentPackage
    {
        /// <summary>
        /// Gets the local project that produces this package or null for external packages.
        /// When a local project exists, this <see cref="FullName"/> is the <see cref="ProjectBase.Name"/>
        /// and this <see cref="Version"/> is null.
        /// </summary>
        Project Project { get; }

        /// <summary>
        /// Gets the package identifier.
        /// </summary>
        string PackageId { get; }

        /// <summary>
        /// Gets the referenced version.
        /// This is null for a locally published project.
        /// </summary>
        SVersion Version { get; }

        /// <summary>
        /// Gets <see cref="PackageId"/>/<see cref="Version"/> for external packages
        /// and the versionless package identifier if the project is locally published.
        /// </summary>
        string FullName { get; }
    }
}
