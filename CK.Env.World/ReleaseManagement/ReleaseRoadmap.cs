using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Enables road map edition.
    /// </summary>
    public class ReleaseRoadmap
    {
        readonly ReleaseSolutionInfo[] _infos;

        ReleaseRoadmap( IDependentSolutionContext ctx, ReleaseSolutionInfo[] infos )
        {
            DependentSolutionContext = ctx;
            _infos = infos;
            foreach( var i in infos ) i.Initialize( this );
        }

        /// <summary>
        /// Gets the dependent solution context.
        /// </summary>
        public IDependentSolutionContext DependentSolutionContext { get; }

        /// <summary>
        /// Gets whether all <see cref="ReleaseSolutionInfo"/> are valid.
        /// </summary>
        public bool IsValid => _infos.All( r => r.CurrentReleaseInfo.IsValid );

        /// <summary>
        /// Helper to find a <see cref="ReleaseSolutionInfo"/> by name.
        /// </summary>
        /// <param name="uniqueSolutionName">The solution name.</param>
        /// <returns>The release info or null if not found.</returns>
        public ReleaseSolutionInfo FindByName( string uniqueSolutionName ) => _infos.FirstOrDefault( r => r.Solution.UniqueSolutionName == uniqueSolutionName );

        /// <summary>
        /// Helper to find the <see cref="ReleaseSolutionInfo"/> for a given solution.
        /// </summary>
        /// <param name="s">The solution.</param>
        /// <returns>The release info or null if not found.</returns>
        public ReleaseSolutionInfo FindBySolution( IDependentSolution s ) => _infos.FirstOrDefault( r => r.Solution == s );

        /// <summary>
        /// Gets the list (in the same order as the <see cref="IDependentSolutionContext.Solutions"/>) of
        /// the associated <see cref="ReleaseSolutionInfo"/>.
        /// </summary>
        public IReadOnlyList<ReleaseSolutionInfo> ReleaseInfos => _infos;

        /// <summary>
        /// Computes the road map for all the solutions (ignoring any current configuration).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="versionSelector">The version selector to use.</param>
        /// <returns>True on success, false on error or cancellation..</returns>
        public bool UpdateRoadMap( IActivityMonitor m, IReleaseVersionSelector versionSelector )
        {
            foreach( var info in _infos )
            {
                if( !info.EnsureReleaseInfo( m, versionSelector ).IsValid ) return false;
            }
            return true;
        }

        /// <summary>
        /// Exports this Roadmap in xml format.
        /// </summary>
        /// <returns>The Xml roadmap.</returns>
        public XElement ToXml() => new XElement( "RoadMap", _infos.Select( i => i.ToXml() ) );

        /// <summary>
        /// Creates a new <see cref="ReleaseRoadmap"/> for a <see cref="IDependentSolutionContext"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="ctx">The context.</param>
        /// <returns>Null on error.</returns>
        public static ReleaseRoadmap Create( IActivityMonitor m, IDependentSolutionContext ctx, XElement previous = null )
        {
            if( ctx == null ) throw new ArgumentNullException( nameof( ctx ) );
            ReleaseSolutionInfo[] infos = new ReleaseSolutionInfo[ctx.Solutions.Count];
            foreach( var s in ctx.Solutions )
            {
                if( s.BranchName == null )
                {
                    m.Error( $"Solution {s.UniqueSolutionName} has no branch name defined." );
                    return null;
                }
                var v = s.GitRepository.GetCommitVersionInfo( m, s.BranchName );
                if( v == null )
                {
                    m.Error( $"Unable to get Commit version information for solution {s.UniqueSolutionName}." );
                    return null;
                }
                infos[s.Index] = new ReleaseSolutionInfo( s, v, previous?
                                                                    .Elements()
                                                                    .FirstOrDefault( e => (string)e.Attribute( "Name" ) == s.UniqueSolutionName ) );
            }
            return new ReleaseRoadmap( ctx, infos );
        }
    }
}
