using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Enables road map edition.
    /// </summary>
    public class ReleaseRoadmap
    {
        readonly ReleaseSolutionInfo[] _infos;

        ReleaseRoadmap( IWorldSolutionContext ctx, ReleaseSolutionInfo[] infos )
        {
            SolutionContext = ctx;
            _infos = infos;
            foreach( var i in infos ) i.Initialize( this );
        }

        internal ReleaseSolutionInfo GetReleaseInfo( int idx ) => _infos[idx];

        /// <summary>
        /// Gets the solution context.
        /// </summary>
        public IWorldSolutionContext SolutionContext { get; }

        /// <summary>
        /// Gets whether this roadmap is valid: all versions have been determined
        /// by at least one successful <see cref="UpdateRoadmap"/>.
        /// </summary>
        public bool IsValid => _infos.All( r => r.CurrentReleaseInfo.IsValid );

        /// <summary>
        /// Gets the list (in the same order as the <see cref="IWorldSolutionContext.Solutions"/>) of
        /// the associated <see cref="ReleaseSolutionInfo"/>.
        /// </summary>
        public IReadOnlyList<IReleaseSolutionInfo> ReleaseInfos => _infos;

        /// <summary>
        /// Computes the road map for all the solutions.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="versionSelector">The version selector to use.</param>
        /// <param name="forgetAllExistingRoadmapVersions">
        /// True to automatically skip any previously reolved versions.
        /// <paramref name="versionSelector"/> will not see them.
        /// </param>
        /// <returns>True on success, false on error or cancellation..</returns>
        public bool UpdateRoadmap( IActivityMonitor m, IReleaseVersionSelector versionSelector, bool forgetAllExistingRoadmapVersions )
        {
            if( !forgetAllExistingRoadmapVersions )
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
        /// Gets the release notes infos.
        /// </summary>
        /// <returns>The release notes.</returns>
        public IReadOnlyList<ReleaseNoteInfo> GetReleaseNotes()
        {
            return _infos.Select( i => new ReleaseNoteInfo( i ) ).ToList();
        }

        /// <summary>
        /// Exports this Roadmap in xml format.
        /// </summary>
        /// <returns>The Xml roadmap.</returns>
        public XElement ToXml() => new XElement( "Roadmap", _infos.Select( i => i.ToXml() ) );

        /// <summary>
        /// Extracts current information from xml roadmap.
        /// </summary>
        /// <param name="e">The Roadmap element.</param>
        /// <returns>Solutions, Git path and <see cref="ReleaseInfo"/>.</returns>
        public static IEnumerable<(string SolutionName, NormalizedPath SubPath, ReleaseInfo Info)> Load( XElement e )
        {
            return e.Elements().Select( s => ((string)s.AttributeRequired( "Name" ), new NormalizedPath( (string)s.AttributeRequired( "SubPath" ) ), new ReleaseInfo( s.Element( "ReleaseInfo" ) )) );
        }

        /// <summary>
        /// Creates a new <see cref="ReleaseRoadmap"/> for a <see cref="IWorldSolutionContext"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="ctx">The context.</param>
        /// <returns>Null on error.</returns>
        public static ReleaseRoadmap Create(
            IActivityMonitor m,
            IWorldSolutionContext ctx,
            XElement previous = null )
        {
            if( ctx == null ) throw new ArgumentNullException( nameof( ctx ) );
            ReleaseSolutionInfo[] infos = new ReleaseSolutionInfo[ctx.Solutions.Count];
            foreach( var (s, d) in ctx.Solutions )
            {
                var v = d.GitRepository.ReadVersionInfo( m );
                if( v == null )
                {
                    m.Error( $"Unable to get Commit version information for solution {s.Solution}." );
                    return null;
                }
                infos[s.Index] = new ReleaseSolutionInfo( d.GitRepository,
                                                          s,
                                                          v,
                                                          previous?
                                                            .Elements()
                                                            .FirstOrDefault( e => (string)e.Attribute( "Name" ) == s.Solution.Name ) );
            }
            return new ReleaseRoadmap( ctx, infos );
        }
    }
}
