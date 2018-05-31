using CK.Setup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Captures Build Projects information that allows to handle ZeroVersion builds and references.
    /// This considers build projects and their dependencies without Solution containment.
    /// </summary>
    public class BuildProjectsInfo
    {

        internal BuildProjectsInfo(
            IDependencySorterResult sortResult,
            IReadOnlyList<(int Rank, IDependentProject Project)> dependenciesToBuild,
            IReadOnlyList<(IDependentProject Project, IReadOnlyList<IDependentPackage> Packages)> projectsToUpgrade )
        {
            Debug.Assert( sortResult != null );
            Debug.Assert( sortResult.IsComplete == (dependenciesToBuild != null && projectsToUpgrade != null) );
            RawBuildProjectsInfoSorterResult = sortResult;
            DependenciesToBuild = dependenciesToBuild ?? Array.Empty<(int Rank, IDependentProject Project)>();
            ProjectsToUpgrade = projectsToUpgrade ?? Array.Empty<(IDependentProject Project, IReadOnlyList<IDependentPackage> Packages)>();
        }

        /// <summary>
        /// Gets the <see cref="IDependencySorterResult"/> of the build projects graph.
        /// Never null.
        /// </summary>
        public IDependencySorterResult RawBuildProjectsInfoSorterResult { get; }

        /// <summary>
        /// Gets whether build projects have been successfully ordered.
        /// </summary>
        public bool HasError => !RawBuildProjectsInfoSorterResult.IsComplete;

        /// <summary>
        /// Gets the projects that must be built: these are all projects that are referenced by
        /// at least one build project and that generate a package.
        /// Never null. Empty if <see cref="HasError"/> is true.
        /// </summary>
        public IReadOnlyList<(int Rank, IDependentProject Project)> DependenciesToBuild { get; }

        /// <summary>
        /// Gets the projects that references at least one of the <see cref="DependenciesToBuild"/> as
        /// a package reference with their respective referenced packages.
        /// Never null. Empty if <see cref="HasError"/> is true.
        /// </summary>
        public IReadOnlyList<(IDependentProject Project, IReadOnlyList<IDependentPackage> Packages)> ProjectsToUpgrade { get; }

    }
}
