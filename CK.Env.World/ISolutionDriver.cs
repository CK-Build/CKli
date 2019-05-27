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
        /// Gets the Git repository.
        /// This can never be null.
        /// </summary>
        IGitRepository GitRepository { get; }

        /// <summary>
        /// Gets the branch name.
        /// This can never be null.
        /// </summary>
        string BranchName { get; }

        /// <summary>
        /// Gets the solution driver of the <see cref="IGitRepository.CurrentBranchName"/>.
        /// </summary>
        /// <returns>This solution driver or the one of the current branch.</returns>
        ISolutionDriver GetCurrentBranchDriver();

        /// <summary>
        /// Gets the Solution that this driver handles.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="reloadSolution">The solution names or null on error.</param>
        /// <returns>The updated sol</returns>
        ISolution GetSolution( IActivityMonitor monitor, bool reloadSolution );

        /// <summary>
        /// Updates projects dependencies and saves the solution and its updated projects.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageInfos">The updates.</param>
        /// <returns>True on success, false on error.</returns>
        bool UpdatePackageDependencies( IActivityMonitor monitor, IReadOnlyCollection<UpdatePackageInfo> packageInfos );

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
