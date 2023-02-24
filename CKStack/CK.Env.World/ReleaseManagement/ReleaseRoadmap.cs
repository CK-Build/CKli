using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Enables road map edition.
    /// </summary>
    public sealed class ReleaseRoadmap
    {
        readonly ReleaseSolutionInfo[] _infos;

        ReleaseRoadmap( IWorldSolutionContext ctx, SVersion worldReleaseVersion, ReleaseSolutionInfo[] infos )
        {
            SolutionContext = ctx;
            WorldReleaseVersion = worldReleaseVersion;
            _infos = infos;
            foreach( var i in infos ) i.Initialize( this );
        }

        internal ReleaseSolutionInfo GetReleaseInfo( int idx ) => _infos[idx];

        /// <summary>
        /// Gets the solution context.
        /// </summary>
        public IWorldSolutionContext SolutionContext { get; }

        /// <summary>
        /// Gets this world release version.
        /// </summary>
        public SVersion WorldReleaseVersion { get; }

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
        /// True to automatically skip any previously resolved versions.
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
        public XElement ToXml() => new XElement( "Roadmap",
                                                 new XAttribute( "WorldReleaseVersion", WorldReleaseVersion ),
                                                 _infos.Select( i => i.ToXml() ) );

        /// <summary>
        /// Extracts current information from xml roadmap.
        /// </summary>
        /// <param name="e">The Roadmap element.</param>
        /// <returns>Solutions, Git path and <see cref="ReleaseInfo"/>.</returns>
        public static IEnumerable<(string SolutionName, NormalizedPath SubPath, ReleaseInfo Info)> LoadSolutionInfos( XElement e )
        {
            return e.Elements().Select( s => ((string)s.AttributeRequired( "Name" ),
                                              new NormalizedPath( (string)s.AttributeRequired( "SubPath" ) ),
                                              new ReleaseInfo( s.Element( "ReleaseInfo" ) )) );
        }

        /// <summary>
        /// Creates a new <see cref="ReleaseRoadmap"/> for a <see cref="IWorldSolutionContext"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="ctx">The context.</param>
        /// <param name="worldReleaseVersion">Optional release version. Computed from the current date.</param>
        /// <param name="previous">Optional previous release. Used to recover previously release info (versions and release notes).</param>
        /// <returns>Null on error.</returns>
        public static ReleaseRoadmap? Create( IActivityMonitor m,
                                              IWorldSolutionContext ctx,
                                              SVersion? worldReleaseVersion = null, 
                                              XElement? previous = null )
        {
            Throw.CheckNotNullArgument( ctx );
            if( worldReleaseVersion == null )
            {
                var now = DateTime.UtcNow;
                // Next CSemVer version allows the null (keep the NRT warning).
                var prevVersion = SVersion.TryParse( (string?)previous?.Attribute( "WorldReleaseVersion" ) );
                bool needFourtPart = prevVersion.IsValid && prevVersion.Major == now.Year && prevVersion.Minor == now.Month && prevVersion.Patch == now.Day;
                int fourthPart = needFourtPart ? prevVersion.FourthPart + 1 : -1;
                worldReleaseVersion = SVersion.Create( now.Year, now.Month, now.Day, handleCSVersion: false, fourthPart: fourthPart );
            }
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
                                                          v.Value,
                                                          previous?
                                                            .Elements()
                                                            .FirstOrDefault( e => (string?)e.Attribute( "Name" ) == s.Solution.Name ) );
            }
            return new ReleaseRoadmap( ctx, worldReleaseVersion, infos );
        }
    }
}
