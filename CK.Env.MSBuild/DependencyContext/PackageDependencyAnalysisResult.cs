using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    public class PackageDependencyAnalysisResult
    {
        internal PackageDependencyAnalysisResult(
            bool? externalDependencies,
            IReadOnlyList<(VersionedPackage Package, IReadOnlyList<IDependentProject> Projects)> monoVersions,
            IReadOnlyList<(VersionedPackage Package, IReadOnlyList<IProjectFramework> Projects)> multiVersions
            )
        {
            WithExternalDependencies = !externalDependencies.HasValue || externalDependencies.Value;
            WithLocalDependencies = !externalDependencies.HasValue || !externalDependencies.Value;
            MonoVersions = monoVersions;
            MultiVersions = multiVersions;
        }

        /// <summary>
        /// Gets whether the external dependencies have been considered.
        /// </summary>
        bool WithExternalDependencies { get; }

        /// <summary>
        /// Gets whether the local dependencies (dependencis to locally published projects) have been considered.
        /// </summary>
        bool WithLocalDependencies { get; }

        /// <summary>
        /// Gets the <see cref="VersionedPackage"/> that are referenced with the same unique version across all projects
        /// (and the projects that reference them).
        /// </summary>
        public IReadOnlyList<(VersionedPackage Package, IReadOnlyList<IDependentProject> Projects)> MonoVersions { get; }

        /// <summary>
        /// Gets the <see cref="VersionedPackage"/> that are referenced by more than one version across all projects.
        /// Each versioned package is associated to the list of the <see cref="IProjectFramework"/> that reference it.
        /// </summary>
        public IReadOnlyList<(VersionedPackage Package, IReadOnlyList<IProjectFramework> Projects)> MultiVersions { get; }




    }
}
