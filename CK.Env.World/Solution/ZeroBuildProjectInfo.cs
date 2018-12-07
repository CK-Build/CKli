using System;
using System.Collections.Generic;
using System.Text;

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
        /// <param name="rank">The project rank.</param>
        /// <param name="solutionName">The solution name. Can not be empty.</param>
        /// <param name="projectName">The project name. Can not be empty.</param>
        /// <param name="mustPack">True if this is a published project.</param>
        /// <param name="upgradePackages">Set of packages reference to upgrade.</param>
        /// <param name="dependencies">List of package dependencies' <see cref="FullName"/> projects.</param>
        public ZeroBuildProjectInfo(
            int rank,
            string solutionName,
            string projectName,
            bool mustPack,
            IReadOnlyCollection<string> upgradePackages,
            IReadOnlyCollection<string> dependencies )
        {
            if( Rank < 0 ) throw new ArgumentException();
            if( String.IsNullOrWhiteSpace( solutionName ) ) throw new ArgumentNullException( nameof( solutionName ) );
            if( String.IsNullOrWhiteSpace( projectName ) ) throw new ArgumentNullException( nameof( projectName ) );
            if( upgradePackages == null ) throw new ArgumentNullException( nameof( upgradePackages ) );
            if( dependencies == null ) throw new ArgumentNullException( nameof( dependencies ) );
            Rank = rank;
            SolutionName = solutionName;
            ProjectName = projectName;
            MustPack = mustPack;
            UpgradePackages = upgradePackages;
            Dependencies = dependencies;
            FullName = mustPack ? projectName : solutionName + '/' + projectName;
        }

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
        /// Gets whether this project must be published in Zero Version.
        /// When false, this project is a Build project that is not used by any other project.
        /// </summary>
        public bool MustPack { get; }

        /// <summary>
        /// Gets the name of the packages that must be updated to the Zero Version.
        /// </summary>
        public IReadOnlyCollection<string> UpgradePackages { get; }

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
