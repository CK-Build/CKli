using CK.Core;
using CK.Build;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace CK.Env
{
    public class BuildResult
    {
        BuildResult(
            BuildResultType type,
            IReadOnlyList<(ArtifactInstance Artifact, string SolutionName, string TargetName)> g,
            IReadOnlyList<ReleaseNoteInfo> releaseNotes )
        {
            Debug.Assert( type != BuildResultType.None );
            Type = type;
            GeneratedArtifacts = g;
            ReleaseNotes = releaseNotes;
            CreationDate = DateTime.UtcNow;
        }

        internal static BuildResult Create(
            IActivityMonitor m,
            BuildResultType type,
            ArtifactCenter artifacts,
            IWorldSolutionContext ctx,
            IReadOnlyList<SVersion> versions,
            IReadOnlyList<ReleaseNoteInfo> releaseNotes )
        {
            var result = new List<(ArtifactInstance Artifact, string SolutionName, string TargetName)>();
            foreach( var row in ctx.DependentSolutions
                                   .Where( s => versions[s.Index] != null )
                                   .SelectMany( s => s.Solution.GeneratedArtifacts.Select( a => (a: a.Artifact.WithVersion( versions[s.Index] ), s) ) ) )
            {
                IArtifactRepository[] handlers = row.s.Solution.ArtifactTargets
                                                     .Where( r => r.Accepts( row.a ) )
                                                     .ToArray();
                if( handlers.Length == 0 )
                {
                    m.Error( $"Unable to find a target artifact repository for {row.a}." );
                    return null;
                }
                foreach( var h in handlers )
                {
                    result.Add( (row.a, row.s.Solution.Name, h.UniqueRepositoryName) );
                }
            }
            return new BuildResult( type, result, releaseNotes );
        }

        /// <summary>
        /// Initializes a new <see cref="BuildResult"/> from a xml element.
        /// </summary>
        /// <param name="e">The xml element.</param>
        public BuildResult( XElement e )
        {
            Type = e.AttributeEnum( XmlNames.xType, BuildResultType.None );
            if( Type == BuildResultType.None )
            {
                throw new ArgumentException( $"Missing BuildResultType." );
            }
            CreationDate = XmlConvert.ToDateTime( (string)e.Attribute( nameof( CreationDate ) ), XmlDateTimeSerializationMode.Utc );
            GeneratedArtifacts = e.Elements( XmlNames.xA )
                                  .Select( a => (
                                            new ArtifactInstance(
                                                    ArtifactType.Single( (string)a.AttributeRequired( XmlNames.xType ) ),
                                                    (string)a.AttributeRequired( XmlNames.xName ),
                                                    SVersion.Parse( (string)a.AttributeRequired( XmlNames.xVersion ) ) ),
                                            (string)a.AttributeRequired( XmlNames.xSolution ),
                                            (string)a.AttributeRequired( XmlNames.xTarget )) )
                                  .ToList();
            var r = e.Element( nameof( ReleaseNotes ) );
            if( r != null )
            {
                ReleaseNotes = r.Elements().Select( n => new ReleaseNoteInfo( n ) ).ToList();
            }
        }

        /// <summary>
        /// Gets the type of the build (Local, CI or Release).
        /// </summary>
        public BuildResultType Type { get; }

        /// <summary>
        /// Gets the <see cref="ArtifactInstance"/> with their originating solution and target artifact repository.
        /// </summary>
        public IReadOnlyList<(ArtifactInstance Artifact, string SolutionName, string TargetName)> GeneratedArtifacts { get; }

        /// <summary>
        /// Gets the release notes infos if this is a release build. Null otherwise.
        /// </summary>
        public IReadOnlyList<ReleaseNoteInfo> ReleaseNotes { get; }

        /// <summary>
        /// Gets the creation time (UTC).
        /// </summary>
        public DateTime CreationDate { get; }

        /// <summary>
        /// Exports this <see cref="BuildResult"/> as xml.
        /// </summary>
        /// <returns>A "BuildResult" element.</returns>
        public XElement ToXml()
        {
            var artifacts = GeneratedArtifacts.Select( a => new XElement( XmlNames.xA,
                                                                   new XAttribute( XmlNames.xType, a.Artifact.Artifact.Type ),
                                                                   new XAttribute( XmlNames.xName, a.Artifact.Artifact.Name ),
                                                                   new XAttribute( XmlNames.xVersion, a.Artifact.Version.ToNormalizedString() ),
                                                                   new XAttribute( XmlNames.xSolution, a.SolutionName ),
                                                                   new XAttribute( XmlNames.xTarget, a.TargetName ) ) );
            var releaseNotes = ReleaseNotes != null
                                ? new XElement( nameof( ReleaseNotes ), ReleaseNotes.Select( r => r.ToXml() ) )
                                : null;

            return new XElement( XmlNames.xBuildResult,
                                    new XAttribute( XmlNames.xType, Type.ToString() ),
                                    new XAttribute( nameof( CreationDate ), CreationDate ),
                                    artifacts,
                                    releaseNotes );
        }

    }
}
