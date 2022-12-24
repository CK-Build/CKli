using CK.Core;
using CK.Env.DependencyModel;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Defines the basic requirement for a solution that the centralized world can handle.
    /// </summary>
    public interface ISolutionDriver
    {
        /// <summary>
        /// Specifies that target frameworks must use the configured Solution Specification PrimaryTargetFramework.
        /// </summary>
        const string UsePrimaryTargetFramework = "UsePrimaryTargetFramework";

        /// <summary>
        /// Gets the Git repository.
        /// This can never be null.
        /// </summary>
        GitRepository GitRepository { get; }

        /// <summary>
        /// Gets the branch name.
        /// This can never be null.
        /// </summary>
        string BranchName { get; }

        /// <summary>
        /// Gets the solution driver of the <see cref="GitRepository.CurrentBranchName"/>.
        /// </summary>
        /// <returns>This solution driver or the one of the current branch.</returns>
        ISolutionDriver GetCurrentBranchDriver();

        /// <summary>
        /// Gets the Solution that this driver handles.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="reloadSolution">True to force a reload of the solution.</param>
        /// <param name="allowInvalidSolution">
        /// True to allow <see cref="IsSolutionValid"/> to be false: the instance is returned as long as the <see cref="ISolution"/> instance has
        /// successfully been built even if some required checks have failed.
        /// </param>
        /// <returns>The solution or null if unable to load the solution.</returns>
        ISolution? GetSolution( IActivityMonitor monitor, bool allowInvalidSolution, bool reloadSolution );

        /// <summary>
        /// Gets whether the Solution that this driver handles is valid or not.
        /// </summary>
        bool IsSolutionValid { get; }

        /// <summary>
        /// Updates projects dependencies and saves the solution and its updated projects.
        /// A build project always update all its (single!) TargetFramework.
        /// </summary>
        /// <remarks>
        /// By default (null <paramref name="frameworkFilter"/>) all package references will be updated
        /// regardless of any framework conditions that are not "locked" (see <see cref="SVersionLock"/>:
        /// NuGet like "[14.2.1]" and Npm references like "=1.2.3" or simply "1.2.3" are locked).
        /// <para>
        /// Filter can be a ';' separated list of target frameworks that are eventually resolved into <see cref="MSProject.Savors"/>
        /// context.
        /// </para>
        /// <para>
        /// Use the special <see cref="UsePrimaryTargetFramework"/> string to restrict the update to
        /// conditions that satisfy <see cref="SharedSolutionSpec.PrimaryTargetFramework"/>.
        /// </para>
        /// </remarks>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageInfos">The update infos.</param>
        /// <param name="frameworkFilter"> See remarks.</param>
        /// <returns>True on success, false otherwise.</returns>
        bool UpdatePackageDependencies( IActivityMonitor monitor, IReadOnlyCollection<UpdatePackageInfo> packageInfos, string? frameworkFilter = null );

        /// <summary>
        /// Builds the solution from its file state.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="withUnitTest">True to execute the unit tests, false to skip unit tests.</param>
        /// <param name="withZeroBuilder">
        /// True to use the Zero Version published build project executable (an error is raised if it has not been already built),
        /// false to compile and run the CodeCakeBuilder project and null to use the Zero Version published build project
        /// if it exists instead of the more costly 'dotnet run'.
        /// </param>
        /// <param name="withPushToRemote">
        /// True to ask the builder to push artifacts to remotes.
        /// This is possible only when building a CI version and applies only to CI versions.
        /// </param>
        /// <returns>True on success, false on error.</returns>
        bool Build( IActivityMonitor monitor, bool withUnitTest, bool? withZeroBuilder, bool withPushToRemote );

        /// <summary>
        /// Builds or publishes the given project (that must be handled by this driver otherwise an exception is thrown)
        /// with a zero version.
        /// This uses "dotnet pack" or "dotnet publish" depending on <see cref="ZeroBuildProjectInfo.MustPack"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="info">The <see cref="ZeroBuildProjectInfo"/>.</param>
        /// <returns>True on success, false on error.</returns>
        bool ZeroBuildProject( IActivityMonitor monitor, ZeroBuildProjectInfo info );

    }
}
