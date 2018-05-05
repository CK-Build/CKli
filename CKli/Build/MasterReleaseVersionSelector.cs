using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CK.Env;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;

namespace CKli
{
    public class MasterReleaseVersionSelector : IReleaseVersionSelector
    {
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
        public ReleaseLevel GetPreReleaseSingleVersionFixActualLevel(
            IActivityMonitor m,
            SolutionDependencyResult.DependentSolution solution,
            CSVersion v,
            bool mustAnswerBetweenFeatureAndBreakingChange )
        {
            return ReleaseLevel.BreakingChange;
        }

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
        public ReleaseLevel GetZeroMajorSingleVersionFeatureActualLevel( IActivityMonitor m, SolutionDependencyResult.DependentSolution solution, CSVersion v )
        {
            return ReleaseLevel.BreakingChange;
        }

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
        public ReleaseLevel ChooseReleaseLevel( IActivityMonitor m, SolutionDependencyResult.DependentSolution solution, ReleaseLevel currentLevel )
        {
            return ReleaseLevel.BreakingChange;
        }

        /// <summary>
        /// This method must finally choose between the remaining possible versions.
        /// This method may return null to cancel the process.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solution">The solution.</param>
        /// <param name="possibleVersions">Possible versions.</param>
        /// <param name="current">Current release information.</param>
        /// <returns>The version to release or null to cancel the process.</returns>
        public CSVersion ChooseFinalVersion( IActivityMonitor m, SolutionDependencyResult.DependentSolution solution, IReadOnlyList<CSVersion> possibleVersions, ReleaseInfo current )
        {
            Console.WriteLine( $"Solution {solution.Solution.UniqueSolutionName}, release information: {current} " );
            var releases = possibleVersions.Where( v => !v.IsPrerelease ).ToList();
            if( releases.Count == 0 )
            {
                m.Error( $"There is no Official versions available. Available were: {possibleVersions.Select( v => v.ToString() ).Concatenate()}" );
                return null;
            }
            if( releases.Count == 1 )
            {
                m.Error( $"Only one Official versions available {releases[0]}." );
                return releases[0];
            }
            for( int i = 0; i < releases.Count; ++i )
            {
                Console.WriteLine( $" {i} - {releases[i]}" );
            }
            Console.WriteLine( $" X - Cancel." );
            for( ; ; )
            {
                Console.Write( $"(Enter the final release number and press enter)> " );
                string line = Console.ReadLine();
                if( line == "X" ) return null;
                if( Int32.TryParse( line, out var num ) && num >= 0 && num < possibleVersions.Count )
                {
                    return releases[num];
                }
                Console.WriteLine();
            }
        }
    }
}
