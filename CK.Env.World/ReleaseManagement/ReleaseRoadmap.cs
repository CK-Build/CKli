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

        internal ReleaseSolutionInfo GetReleaseInfo( int idx ) => _infos[idx];

        /// <summary>
        /// Gets the dependent solution context.
        /// </summary>
        public IDependentSolutionContext DependentSolutionContext { get; }

        /// <summary>
        /// Gets whether this roadmap is valid: all versions have been determined
        /// by at least one successful <see cref="UpdateRoadmap"/>.
        /// </summary>
        public bool IsValid => _infos.All( r => r.CurrentReleaseInfo.IsValid );

        /// <summary>
        /// Gets the list (in the same order as the <see cref="IDependentSolutionContext.Solutions"/>) of
        /// the associated <see cref="ReleaseSolutionInfo"/>.
        /// </summary>
        public IReadOnlyList<IReleaseSolutionInfo> ReleaseInfos => _infos;

        /// <summary>
        /// Computes the road map for all the solutions.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="versionSelector">The version selector to use.</param>
        /// <param name="skipPreviouslyResolved">
        /// True to automatically skip any previously reolved versions.
        /// <paramref name="versionSelector"/> will not see them.
        /// </param>
        /// <returns>True on success, false on error or cancellation..</returns>
        public bool UpdateRoadmap( IActivityMonitor m, IReleaseVersionSelector versionSelector, bool skipPreviouslyResolved )
        {
            if( !skipPreviouslyResolved )
            {
                foreach( var info in _infos )
                {
                    info.ClearReleaseInfo();
                }
            }
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
        public XElement ToXml() => new XElement( "Roadmap", _infos.Select( i => i.ToXml() ) );

        /// <summary>
        /// Creates a new <see cref="ReleaseRoadmap"/> for a <see cref="IDependentSolutionContext"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="ctx">The context.</param>
        /// <returns>Null on error.</returns>
        public static ReleaseRoadmap Create(
            IActivityMonitor m,
            IDependentSolutionContext ctx,
            XElement previous = null,
            bool restorePreviousState = true )
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
