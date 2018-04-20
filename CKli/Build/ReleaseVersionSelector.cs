using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CK.Env.MSBuild;
using CSemVer;

namespace CKli
{


    public class ChooseVersionContext
    {
        public SolutionDependencyResult.DependentSolution Solution { get; }

        public IReadOnlyList<CSVersion> PossibleVersions { get; }

        public ReleaseInfo CurrentReleaseInfo { get; }

        public static IEnumerable<CSVersion> FilterVersions(IEnumerable<CSVersion> possibleVersions, ReleaseConstraint c)
        {
            IEnumerable<CSVersion> filtered = possibleVersions;

            // 1 - Filtering PreRelease if required.
            //     The 0 major is excluded from this filter.
            if( (c & ReleaseConstraint.MustBePreRelease) != 0 )
            {
                filtered = filtered.Where( v => v.Major == 0 || v.IsPreRelease );
            }
            // 2 - If a breaking change or a feature occurred, this can not be a Patch, regardless
            //     of the Official vs. PreRelease status of the version.
            //     This filter is applied to the 0 major since the 0 major can perfectly handle this.
            if( (c & (ReleaseConstraint.HasBreakingChanges | ReleaseConstraint.HasFeatures)) != 0 )
            {
                filtered = filtered.Where( v => !v.IsPatch );
            }
            else
            {
                // When there is no breaking change nor feature, this is necessarily a Patch.
                filtered = filtered.Where( v => v.IsPatch );
            }

            // 3 - On a breaking change, Official version must have their Major bumped (ie. their Minor and Patch must be 0).
            //     The 0 major is excluded from this filter.
            if( (c & ReleaseConstraint.HasBreakingChanges) != 0 )
            {
                filtered = filtered.Where( v => v.Major == 0 || v.IsPreRelease || (v.Minor == 0 && v.Patch == 0) );
            }
            return filtered;
        }

    }

    public class ReleaseVersionSelector
    {
        public ReleaseLevel GetPreReleaseSingleVersionFixActualLevel( IActivityMonitor m, SolutionDependencyResult.DependentSolution solution, CSVersion v )
        {
            Console.WriteLine( $"Only one version is available for {solution.Solution.UniqueSolutionName}: {v}" );
            Console.WriteLine( $" 1 - This version is a just fix." );
            Console.WriteLine( $" 2 - This version introduces a new feature or a breaking change." );
            Console.WriteLine( $" X - Cancel." );
            char c;
            while( "12X".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 ) ;
            if( c == 1 ) return ReleaseLevel.Fix;
            if( c == 2 ) return ReleaseLevel.Feature;
            return ReleaseLevel.None;
        }

        public ReleaseInfo ChooseVersion( IActivityMonitor m, SolutionDependencyResult.DependentSolution solution, IReadOnlyList<CSVersion> versions, ReleaseInfo current )
        {
            Console.WriteLine( $"Choosing version for {solution.Solution.UniqueSolutionName} among {versions.Count} possible versions." );
            Console.WriteLine( $"  - Current Release level is {current.Level}." );
            if( current.Level != ReleaseLevel.BreakingChange )
            {
                for( int i = (int)current.Level; i <= (int)ReleaseLevel.BreakingChange; ++i )
                {
                    Console.WriteLine( $"     {i} - {(ReleaseLevel)i}" );
                }
            }
            char c;
            while( (c = Console.ReadKey().KeyChar) != 'X' &&  c < '0' + (int)current.Level && c > '0' + (int)ReleaseLevel.BreakingChange ) ;
            if( c == 'X' ) return new ReleaseInfo();
            ReleaseLevel l = (ReleaseLevel)(c - '0');

        }
    }
}
