using CK.Core;
using CK.Env;
using CK.Env.MSBuild;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env
{
    public class GlobalReleaser
    {
        readonly IReadOnlyList<SolutionReleaser> _releasers;

        internal GlobalReleaser( IReadOnlyList<SolutionReleaser> solutions )
        {
            _releasers = solutions;
            foreach( var s in solutions ) s.Initialize( this );
        }

        public bool IsValid => _releasers.All( r => r.CurrentReleaseInfo.IsValid );

        public SolutionReleaser FindByName( string uniqueSolutionName ) => _releasers.FirstOrDefault( r => r.Solution.Solution.UniqueSolutionName == uniqueSolutionName );

        public SolutionReleaser FindBySolution( Solution s ) => _releasers.FirstOrDefault( r => r.Solution.Solution == s );

        public SolutionReleaser FindBySolution( SolutionDependencyContext.DependentSolution s ) => _releasers.FirstOrDefault( r => r.Solution == s );

        /// <summary>
        /// Computes a partial road map up to a given solution.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="versionSelector">The version selector.</param>
        /// <param name="uniqueSolutionName">The unique solution name. See <see cref="Solution.UniqueSolutionName"/>.</param>
        /// <param name="buildTarget">True to build the targeted solution, false to stop after having upgraded its dependencies.</param>
        /// <returns>The Xml roadmap or null on error.</returns>
        public XElement ComputePartialRoadMap(
            IActivityMonitor m,
            IReleaseVersionSelector versionSelector,
            string uniqueSolutionName,
            bool buildTarget )
        {
            var target = FindByName( uniqueSolutionName );
            if( target == null )
            {
                m.Error( $"Unable to find solution '{uniqueSolutionName}'. Available solutions are: {_releasers.Select( r => r.Solution.Solution.UniqueSolutionName ).Concatenate()}" );
                return null;
            }
            var result = new XElement( "RoadMap", new XAttribute( "Target", target.Solution.Solution.UniqueSolutionName ) );
            if( !target.EnsureReleaseInfo( m, versionSelector, rNeeded => result.Add( rNeeded.ToXml( rNeeded != target || buildTarget ) ) ).IsValid ) return null;
            return result;
        }

        /// <summary>
        /// Computes the full road map of all the solutions.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="versionSelector">The version selector to use.</param>
        /// <returns>The Xml roadmap or null on error or cancellation.</returns>
        public XElement ComputeFullRoadMap( IActivityMonitor m, IReleaseVersionSelector versionSelector )
        {
            var result = new XElement( "RoadMap" );
            foreach( var r in _releasers )
            {
                if( !r.EnsureReleaseInfo( m, versionSelector, rNeeded => result.Add( rNeeded.ToXml( true ) ) ).IsValid )
                {
                    return null;
                }
            }
            return result;
        }

    }
}
