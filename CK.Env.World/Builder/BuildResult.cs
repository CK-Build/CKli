using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
            IReadOnlyList<IDependentSolution> solutions,
            IReadOnlyList<SVersion> versions,
            IReadOnlyList<ReleaseNoteInfo> releaseNotes)
        {
            var result = new List<(ArtifactInstance Artifact, string SolutionName, string TargetName)>();
            foreach( var row in solutions.Where( s => versions[s.Index] != null )
                                         .SelectMany( s => s.GeneratedArtifacts.Select( a => (a, s, versions[s.Index]) ) ) )
            {
                IArtifactRepository handler = row.s.ArtifactTargetNames
                                                    .Select( name => artifacts.Find( name ) )
                                                    .FirstOrDefault( r => r.HandleArtifactType( row.a.Type ) );
                if( handler == null )
                {
                    m.Error( $"Unable to find a target artifact repository for {row.a}." );
                    return null;
                }
                result.Add( (new ArtifactInstance( row.a, versions[row.s.Index] ), row.s.UniqueSolutionName, handler.Info.UniqueArtifactRepositoryName) );
            }
            return new BuildResult( type, result, releaseNotes );
        }

        /// <summary>
        /// Initializes a new <see cref="BuildResult"/> from a xml element.
        /// </summary>
        /// <param name="e">The xml element.</param>
        public BuildResult( XElement e )
        {
            Type = e.AttributeEnum( "Type", BuildResultType.None );
            if( Type == BuildResultType.None )
            {
                throw new ArgumentException( $"Missing BuildResultType." );
            }
            CreationDate = XmlConvert.ToDateTime( (string)e.Attribute( nameof(CreationDate) ), XmlDateTimeSerializationMode.Utc );
            GeneratedArtifacts = e.Elements( "A" )
                                  .Select( a => (
                                            new ArtifactInstance(
                                                    (string)a.AttributeRequired("Type"),
                                                    (string)a.AttributeRequired( "Name" ),
                                                    SVersion.Parse( (string)a.AttributeRequired( "Version" ) ) ),
                                            (string)a.AttributeRequired( "Solution" ),
                                            (string)a.AttributeRequired( "Target" ) ) )
                                  .ToList();
            var r = e.Element( nameof(ReleaseNotes) );
            if( r != null )
            {
                ReleaseNotes = r.Elements().Select( n => new ReleaseNoteInfo( n ) ).ToList();
            }
        }

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
            var artifacts = GeneratedArtifacts.Select( a => new XElement( "A",
                                                                   new XAttribute( "Type", a.Artifact.Artifact.Type ),
                                                                   new XAttribute( "Name", a.Artifact.Artifact.Name ),
                                                                   new XAttribute( "Version", a.Artifact.Version.ToNuGetPackageString() ),
                                                                   new XAttribute( "Solution", a.SolutionName ),
                                                                   new XAttribute( "Target", a.TargetName ) ) );
            var releaseNotes = ReleaseNotes != null
                                ? new XElement( nameof(ReleaseNotes), ReleaseNotes.Select( r => r.ToXml() ) )
                                : null;

            return new XElement( "BuildResult",
                                    new XAttribute( "Type", Type.ToString() ),
                                    new XAttribute( nameof(CreationDate), CreationDate ),
                                    artifacts,
                                    releaseNotes );
        }

    }
}
