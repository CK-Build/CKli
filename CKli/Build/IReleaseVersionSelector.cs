using System.Collections.Generic;
using CK.Core;
using CK.Env.MSBuild;
using CSemVer;

namespace CKli
{
    public interface IReleaseVersionSelector
    {
        /// <summary>
        /// This method must choose between an Official or Prerelease versions.
        /// By returning null, this method can cancel the process.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solution">The solution.</param>
        /// <param name="officials">Officials releases possible versions.</param>
        /// <param name="prereleases">Prereleases possible versions</param>
        /// <returns>The selected set of versions or null to cancel the process.</returns>
        IEnumerable<CSVersion> ChooseBetweenOfficialAndPreReleaseVersions( IActivityMonitor m, SolutionDependencyResult.DependentSolution solution, IEnumerable<CSVersion> officials, IEnumerable<CSVersion> prereleases );

        /// <summary>
        /// This method must choose the <see cref="ReleaseLevel"/> for the solution.
        /// It may return <see cref="ReleaseLevel.None"/> to cancel the process, but should return
        /// a level greater or equal to the <paramref name="currentLevel"/> (that is necessarily
        /// <see cref="ReleaseLevel.Fix"/> or <see cref="ReleaseLevel.Feature"/>).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solution">The solution.</param>
        /// <param name="currentLevel">The current release level.</param>
        /// <returns>The selected level.</returns>
        ReleaseLevel ChooseReleaseLevel( IActivityMonitor m, SolutionDependencyResult.DependentSolution solution, ReleaseLevel currentLevel );

        /// <summary>
        /// This method must answer whether a pre-release (or a 0 Major version) actually
        /// introduces new features or breaking changes.
        /// It may return <see cref="ReleaseLevel.None"/> to cancel the process.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solution">The solution.</param>
        /// <param name="v">The only possible version.</param>
        /// <param name="mustAnswerBetweenFeatureAndBreakingChange">
        /// True if the choice between <see cref="ReleaseLevel.Feature"/> and <see cref="ReleaseLevel.BreakingChange"/> is required.
        /// This is false for true prereleases (not a zero major version) since for prereleases breaking changes are allowed.
        /// </param>
        /// <returns>The level to consider for the release.</returns>
        ReleaseLevel GetPreReleaseSingleVersionFixActualLevel( IActivityMonitor m, SolutionDependencyResult.DependentSolution solution, CSVersion v, bool mustAnswerBetweenFeatureAndBreakingChange );

        /// <summary>
        /// This method must handle the zero major in Feature level edge case.
        /// It may return <see cref="ReleaseLevel.None"/> to cancel the process, but should return
        /// either <see cref="ReleaseLevel.Feature"/> (no change) or <see cref="ReleaseLevel.BreakingChange"/>
        /// if the solution actually introduces a breaking change.
        /// In no way should this method downgrade the level to <see cref="ReleaseLevel.Fix"/> (this would throw an <see cref="InvalidOperationException"/>).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solution">The current solution for which the version has a zero major.</param>
        /// <param name="v">The version (with a zero major).</param>
        /// <returns>The level.</returns>
        ReleaseLevel GetZeroMajorSingleVersionFeatureActualLevel( IActivityMonitor m, SolutionDependencyResult.DependentSolution solution, CSVersion v );

        /// <summary>
        /// This method must finally choose between the remaining possible versions.
        /// This method may return null to cancel the process.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solution">The solution.</param>
        /// <param name="possibleVersions">Possible versions.</param>
        /// <param name="current">Current release information.</param>
        /// <returns>The version to release or null to cancel the process.</returns>
        CSVersion ChooseFinalVersion( IActivityMonitor m, SolutionDependencyResult.DependentSolution solution, IReadOnlyList<CSVersion> possibleVersions, ReleaseInfo current );

    }
}
