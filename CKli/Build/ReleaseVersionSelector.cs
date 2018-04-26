using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;

namespace CKli
{
    public class ReleaseVersionSelector : IReleaseVersionSelector
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
        public IEnumerable<CSVersion> ChooseBetweenOfficialAndPreReleaseVersions( IActivityMonitor m, SolutionDependencyResult.DependentSolution solution, IEnumerable<CSVersion> officials, IEnumerable<CSVersion> prereleases )
        {
            Console.WriteLine( "=========" );
            Console.WriteLine( $" Choose between Official and PreRelease version for '{solution.Solution.UniqueSolutionName}':" );
            Console.WriteLine( $" 1 - Official releases: {officials.Select( v => v.ToString() ).Concatenate()}." );
            Console.WriteLine( $" 2 - Pre-releases: {prereleases.Select( v => v.ToString() ).Concatenate()}." );
            Console.WriteLine( $" X - Cancel." );
            char c;
            while( "12X".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 ) ;
            if( c == 'X' ) return null;
            return c == '1' ? officials : prereleases;
        }

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
        public ReleaseLevel GetPreReleaseSingleVersionFixActualLevel(
            IActivityMonitor m,
            SolutionDependencyResult.DependentSolution solution,
            CSVersion v,
            bool mustAnswerBetweenFeatureAndBreakingChange )
        {
            Console.WriteLine( "=========" );
            Console.WriteLine( $"Only one version is available for '{solution.Solution.UniqueSolutionName}': {v}" );
            Console.WriteLine( $" 1 - This version only fixes bugs." );
            if( !mustAnswerBetweenFeatureAndBreakingChange )
            {
                Console.WriteLine( $" 2 - This version introduces new features or breaking changes." );
            }
            else
            {
                Console.WriteLine( $" 2 - This version introduces new features." );
                Console.WriteLine( $" 3 - This version introduces breaking changes." );
            }
            Console.WriteLine( $" X - Cancel." );
            char c;
            while( "123X".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 && (!mustAnswerBetweenFeatureAndBreakingChange || c == '3') ) ;
            if( c == '1' ) return ReleaseLevel.Fix;
            if( c == '2' ) return ReleaseLevel.Feature;
            if( c == '3' ) return ReleaseLevel.BreakingChange;
            return ReleaseLevel.None;
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
            Console.WriteLine( "=========" );
            Console.WriteLine( $"Solution '{solution.Solution.UniqueSolutionName}' will be released with version {v} that has 0 as its Major." );
            Console.WriteLine( $"We need to know whether this release introduces a breaking change:" );
            Console.WriteLine( $" 1 - Yes, this release introduces a breaking change:" );
            Console.WriteLine( $" 2 - No, this is only a feature release." );
            Console.WriteLine( $" X - Cancel." );
            char c;
            while( "12X".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 ) ;
            if( c == 'X' ) return ReleaseLevel.None;
            return c == '1' ? ReleaseLevel.BreakingChange : ReleaseLevel.Feature;
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
            Console.WriteLine( "=========" );
            Console.WriteLine( $"Solution {solution.Solution.UniqueSolutionName}:" );
            if( currentLevel == ReleaseLevel.Fix )
            {
                Console.WriteLine( $" 1 - This release only fixes bugs." );
                Console.WriteLine( $" 2 - This release introduces new features." );
                Console.WriteLine( $" 3 - This release introduces breaking changes." );
            }
            else 
            {
                Console.WriteLine( $" 1 - This release only fixes bugs or introduces new features." );
                Console.WriteLine( $" 2 - This release introduces breaking changes." );
            }
            Console.WriteLine( $" X - Cancel." );
            char c;
            while( "123X".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 && (currentLevel != ReleaseLevel.Fix || c == '3') ) ;
            if( currentLevel == ReleaseLevel.Fix )
            {
                if( c == '1' ) return ReleaseLevel.Fix;
                if( c == '2' ) return ReleaseLevel.Feature;
                if( c == '3' ) return ReleaseLevel.BreakingChange;
            }
            else
            {
                if( c == '1' ) return ReleaseLevel.Feature;
                if( c == '2' ) return ReleaseLevel.BreakingChange;
            }
            return ReleaseLevel.None;
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
            for( int i = 0; i < possibleVersions.Count; ++i )
            {
                Console.WriteLine( $" {i} - {possibleVersions[i]}" );
            }
            Console.WriteLine( $" X - Cancel." );
            for( ; ; )
            {
                Console.Write( $"(Enter the final release number and press enter)> " );
                string line = Console.ReadLine();
                if( line == "X" ) return null;
                if( Int32.TryParse( line, out var num ) && num >= 0 && num < possibleVersions.Count )
                {
                    return possibleVersions[num];
                }
                Console.WriteLine();
            }
        }
    }
}
