using System;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Captures information required to build the Build projects and its dependencies in Zero Version.
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
        /// <param name="solutionName">The solution name. Can not be empty.</param>
        /// <param name="projectName">The project name. Can not be empty.</param>
        /// <param name="mustPack">True if this is a published project.</param>
        /// <param name="upgradePackages">Set of packages name reference to upgrade.</param>
        /// <param name="upgradePackages">Set of packages OA project name reference to upgrade to the Zero version.</param>
        /// <param name="dependencies">List of package dependencies' <see cref="FullName"/> projects.</param>
        public ZeroBuildProjectInfo(
            int index,
            int rank,
            string solutionName,
            string projectName,
            string primarySolutionRelativeFolderPath,
            bool mustPack,
            IReadOnlyCollection<string> upgradePackages,
            IReadOnlyCollection<string> upgradeZeroProjects,
            IReadOnlyCollection<string> dependencies )
        {
            if( Index < 0 ) throw new ArgumentOutOfRangeException( nameof( index ) );
            if( Rank < 0 ) throw new ArgumentOutOfRangeException( nameof( rank ) );
            if( String.IsNullOrWhiteSpace( solutionName ) ) throw new ArgumentNullException( nameof( solutionName ) );
            if( String.IsNullOrWhiteSpace( projectName ) ) throw new ArgumentNullException( nameof( projectName ) );
            if( String.IsNullOrWhiteSpace( primarySolutionRelativeFolderPath ) ) throw new ArgumentNullException( nameof( primarySolutionRelativeFolderPath ) );
            Index = index;
            Rank = rank;
            SolutionName = solutionName;
            ProjectName = projectName;
            PrimarySolutionRelativeFolderPath = primarySolutionRelativeFolderPath;
            MustPack = mustPack;
            UpgradePackages = upgradePackages ?? throw new ArgumentNullException( nameof( upgradePackages ) );
            UpgradeZeroProjects = upgradeZeroProjects ?? throw new ArgumentNullException( nameof( upgradeZeroProjects ) );
            Dependencies = dependencies ?? throw new ArgumentNullException( nameof( dependencies ) );
            FullName = mustPack ? projectName : solutionName + '/' + projectName;
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
        /// Gets the solution name.
        /// </summary>
        public string SolutionName { get; }

        /// <summary>
        /// Gets the project name.
        /// </summary>
        public string ProjectName { get; }

        /// <summary>
        /// Gets the project path.
        /// </summary>
        public string PrimarySolutionRelativeFolderPath { get; }

        /// <summary>
        /// Gets whether this project must be published in Zero Version.
        /// When false, this project is a Build project that is not used by any other project.
        /// </summary>
        public bool MustPack { get; }

        /// <summary>
        /// Gets the name of the packages that must be updated.
        /// This does not contain ProjectReferences.
        /// </summary>
        public IReadOnlyCollection<string> UpgradePackages { get; }

        /// <summary>
        /// Gets the name of the packages that must be updated to the Zero Version.
        /// Caution: This contains the PackageReferences as well as ProjectReferences:
        /// ProjectReferences MUST be transformed into PackageReferences to the Zero version.
        /// </summary>
        public IReadOnlyCollection<string> UpgradeZeroProjects { get; }

        /// <summary>
        /// Gets the <see cref="FullName"/> of the projects that are (transitively) used by this project.
        /// </summary>
        public IReadOnlyCollection<string> Dependencies { get; }

        /// <summary>
        /// Gets the name that identifies this project.
        /// It is the <see cref="ProjectName"/> if <see cref="MustPack"/> is true
        /// (name of the package) and <see cref="SolutionName"/>/<see cref="ProjectName"/> otherwise.
        /// </summary>
        public string FullName { get; }

        /// <summary>
        /// Returns the <see cref="FullName"/>.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => FullName;
    }


}
