using System.Collections.Generic;
using CK.Core;
using CSemVer;

namespace CK.Env
{
    public interface IReleaseVersionSelector
    {
        /// <summary>
        /// When a solution has no obligation to be released (even as a fix), this method may decide
        /// to NOT release it at all and use the last released version.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solution">The solution.</param>
        /// <param name="previousVersion">The previous released version.</param>
        /// <returns>True to use the previous version, false to release at least a fix and null to cancel the process.</returns>
        bool? CanUsePreviousVersion( IActivityMonitor m, IDependentSolution solution, CSVersion previousVersion );

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
        ReleaseLevel ChooseReleaseLevel( IActivityMonitor m, IDependentSolution solution, ReleaseLevel currentLevel );

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
        /// This is false for normal prereleases (not a zero major version) since for normal prereleases breaking changes are allowed.
        /// </param>
        /// <returns>The level to consider for the release.</returns>
        ReleaseLevel GetPreReleaseSingleVersionFixActualLevel( IActivityMonitor m, IDependentSolution solution, CSVersion v, bool mustAnswerBetweenFeatureAndBreakingChange );

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
        ReleaseLevel GetZeroMajorSingleVersionFeatureActualLevel( IActivityMonitor m, IDependentSolution solution, CSVersion v );

        /// <summary>
        /// This method must finally choose between the remaining possible versions.
        /// This method may return null to cancel the process.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solution">The solution.</param>
        /// <param name="possibleVersions">Possible versions.</param>
        /// <param name="current">Current release information.</param>
        /// <returns>The version to release or null to cancel the process.</returns>
        CSVersion ChooseFinalVersion( IActivityMonitor m, IDependentSolution solution, IReadOnlyList<CSVersion> possibleVersions, ReleaseInfo current );
    }
}
