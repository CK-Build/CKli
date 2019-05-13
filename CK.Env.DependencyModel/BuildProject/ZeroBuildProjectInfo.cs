using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Captures information required to build the Build projects and their dependencies in Zero Version.
    /// This is computed by analyzing the the pure build projects dependency graph (ignoring Solutions) that is
    /// the transitive closure of all Solution's build projects.
    /// </summary>
    public class ZeroBuildProjectInfo
    {
        /// <summary>
        /// Initializes a new <see cref="ZeroBuildProjectInfo"/>.
        /// </summary>
        /// <param name="index">The project index in <see cref="IDependentSolutionContext.BuildProjectsInfo"/> ordered list.</param>
        /// <param name="rank">The project rank.</param>
        /// <param name="project">The project itself.</param>
        /// <param name="upgradePackages">Set of projects for which package references must be upgraded.</param>
        /// <param name="upgradeZeroProjects">
        /// All the published projects: the ones who are actually referenced as a package AND the
        /// ones that are ProjectReference.
        /// ProjectReference MUST be transformed into PackageReference during ZeroBuild.
        /// </param>
        /// <param name="dependencies">List of all the dependencies.</param>
        public ZeroBuildProjectInfo(
            int index,
            int rank,
            IProject project,
            IReadOnlyCollection<IProject> upgradeProjectPackages,
            IReadOnlyCollection<IProject> upgradeZeroProjects,
            IReadOnlyCollection<IProject> allDependencies )
        {
            if( Index < 0 ) throw new ArgumentOutOfRangeException( nameof( index ) );
            if( Rank < 0 ) throw new ArgumentOutOfRangeException( nameof( rank ) );
            Index = index;
            Rank = rank;
            Project = project;
            UpgradeProjectPackages = upgradeProjectPackages ?? throw new ArgumentNullException( nameof( upgradeProjectPackages ) );
            UpgradeZeroProjects = upgradeZeroProjects ?? throw new ArgumentNullException( nameof( upgradeZeroProjects ) );
            AllDependencies = allDependencies ?? throw new ArgumentNullException( nameof( allDependencies ) );
        }

        /// <summary>
        /// Gets the project index in <see cref="IDependentSolutionContext.BuildProjectsInfo"/> ordered list.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets the rank of this build project in the pure build projects dependency graph.
        /// </summary>
        public int Rank { get; }

        /// <summary>
        /// Gets the project.
        /// </summary>
        public IProject Project { get; }

        /// <summary>
        /// Gets whether this project must be packed, ie. it is used as a package
        /// by actual build projects: any project that is not a <see cref="IProject.IsBuildProject"/>
        /// must be packed.
        /// </summary>
        public bool MustPack => !Project.IsBuildProject;

        /// <summary>
        /// Gets all the projects for which package references to any generated artifacts must be updated.
        /// This does not contain ProjectReferences.
        /// </summary>
        public IReadOnlyCollection<IProject> UpgradeProjectPackages { get; }

        /// <summary>
        /// Gets the package references to any generated artifacts that must be updated.
        /// This does not contain ProjectReferences.
        /// </summary>
        public IEnumerable<Artifact> UpgradePackages => UpgradeProjectPackages.SelectMany( p => p.GeneratedArtifacts.Select( g => g.Artifact ) );

        /// <summary>
        /// Gets the projects for which all references must be updated to the Zero Version.
        /// Caution: This is about PackageReferences as well as ProjectReferences: ProjectReferences MUST be transformed
        /// into PackageReferences to the Zero version.
        /// </summary>
        public IReadOnlyCollection<IProject> UpgradeZeroProjects { get; }

        /// <summary>
        /// Gets all the projects that are (transitively) used by this project.
        /// </summary>
        public IReadOnlyCollection<IProject> AllDependencies { get; }

        /// <summary>
        /// Returns the project's name.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => Project.Name;
    }


}
