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
        BuildResult( BuildResultType type,
                     SVersion? worldReleaseVersion,
                     IReadOnlyList<(ArtifactInstance Artifact, string TargetName)> artifacts,
                     (string SolutionName, SVersion Version, string? CommitSha)[] solutions,
                     IReadOnlyList<ReleaseNoteInfo>? releaseNotes )
        {
            Debug.Assert( type != BuildResultType.None );
            Type = type;
            WorldReleaseVersion = worldReleaseVersion;
            GeneratedArtifacts = artifacts;
            Solutions = solutions;
            ReleaseNotes = releaseNotes;
            CreationDate = DateTime.UtcNow;
        }

        internal static BuildResult? Create( IActivityMonitor m,
                                             BuildResultType type,
                                             SVersion? worldReleaseVersion,
                                             IWorldSolutionContext ctx,
                                             IReadOnlyList<SVersion?> versions,
                                             IReadOnlyList<ReleaseNoteInfo>? releaseNotes )
        {
            Throw.CheckState( (type == BuildResultType.Release) == (worldReleaseVersion?.IsValid is true), "Release <==> WorldReleaseVersion" );

            var solutions = new List<(string SolutionName, SVersion Version, string? CommitSha)>();
            var artifacts = new List<(ArtifactInstance Artifact, string TargetName)>();

            foreach( var s in ctx.DependentSolutions.Where( s => versions[s.Index] != null ) )
            {
                var sVersion = versions[s.Index]!;
                solutions.Add( (s.Solution.Name, sVersion, ctx.Drivers[s.Index].GitRepository.Head.CommitSha) );
                foreach( var a in s.Solution.GeneratedArtifacts )
                {
                    var vA = a.Artifact.WithVersion( sVersion );
                    IArtifactRepository[] handlers = s.Solution.ArtifactTargets
                                                     .Where( r => r.Accepts( vA ) )
                                                     .ToArray();
                    if( handlers.Length == 0 )
                    {
                        m.Error( $"Unable to find a target artifact repository for {a}." );
                        return null;
                    }
                    foreach( var h in handlers )
                    {
                        artifacts.Add( (vA, h.UniqueRepositoryName) );
                    }
                }
            }
            return new BuildResult( type, worldReleaseVersion, artifacts, solutions.ToArray(), releaseNotes );
        }

        /// <summary>
        /// Initializes a new <see cref="BuildResult"/> from a xml element.
        /// </summary>
        /// <param name="e">The xml element.</param>
        public BuildResult( XElement e )
        {
            Type = e.AttributeEnum( XmlNames.xType, BuildResultType.None );
            Throw.CheckData( Type != BuildResultType.None );

            CreationDate = XmlConvert.ToDateTime( (string)e.AttributeRequired( XmlNames.xCreationDate ), XmlDateTimeSerializationMode.Utc );

            int version = (int?)e.Attribute( XmlNames.xVersion ) ?? 0;
            if( Type == BuildResultType.Release )
            {
                if( version == 0 )
                {
                    WorldReleaseVersion = SVersion.Create( CreationDate.Year, CreationDate.Month, CreationDate.Day );
                }
                else
                {
                    WorldReleaseVersion = SVersion.Parse( (string)e.AttributeRequired( XmlNames.xWorldReleaseVersion ), handleCSVersion: false );
                }
            }

            GeneratedArtifacts = e.Elements( XmlNames.xA )
                                  .Select( a => (
                                            new ArtifactInstance(
                                                    ArtifactType.Single( (string)a.AttributeRequired( XmlNames.xType ) ),
                                                    (string)a.AttributeRequired( XmlNames.xName ),
                                                    SVersion.Parse( (string)a.AttributeRequired( XmlNames.xVersion ) ) ),
                                            (string)a.AttributeRequired( XmlNames.xTarget )) )
                                  .ToList();
            var r = e.Element( nameof( ReleaseNotes ) );
            if( r != null )
            {
                ReleaseNotes = r.Elements().Select( n => new ReleaseNoteInfo( n ) ).ToList();
            }

            if( version == 0 )
            {
                Solutions = e.Elements( XmlNames.xA )
                              .Select( a => ((string)a.AttributeRequired( XmlNames.xSolution ), SVersion.Parse( (string)a.AttributeRequired( XmlNames.xVersion ) ), (string?)null) )
                              .ToArray();
            }
            else
            {
                Solutions = e.Elements( XmlNames.xS )
                             .Select( a => ((string)a.AttributeRequired( XmlNames.xName ),
                                            SVersion.Parse( (string)a.AttributeRequired( XmlNames.xVersion ) ),
                                            (string?)e.AttributeRequired( XmlNames.xCommitSha ) ) )
                             .ToArray();
            }
        }

        /// <summary>
        /// Gets the type of the build (Local, CI or Release).
        /// </summary>
        public BuildResultType Type { get; }

        /// <summary>
        /// Gets the non null world release version if this is a <see cref="BuildResultType.Release"/>.
        /// </summary>
        public SVersion? WorldReleaseVersion { get; }

        /// <summary>
        /// Gets the <see cref="ArtifactInstance"/> with their originating solution and target artifact repository.
        /// </summary>
        public IReadOnlyList<(ArtifactInstance Artifact, string TargetName)> GeneratedArtifacts { get; }

        /// <summary>
        /// Gets all the solution names, produced version and Commit identifier for the build.
        /// Old (Version 0) BuildResults have null CommitSha.
        /// This is temporarily an array so that CommitSha can be fixed for release builds (Version 0 => 1).
        /// </summary>
        public (string SolutionName, SVersion Version, string? CommitSha)[] Solutions { get; }

        /// <summary>
        /// Gets the release notes infos if this is a release build. Null otherwise.
        /// </summary>
        public IReadOnlyList<ReleaseNoteInfo>? ReleaseNotes { get; }

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
            Throw.CheckState( (Type == BuildResultType.Release) == (WorldReleaseVersion?.IsValid is true), "Release <==> WorldReleaseVersion" );
            var artifacts = GeneratedArtifacts.Select( a => new XElement( XmlNames.xA,
                                                                   new XAttribute( XmlNames.xType, a.Artifact.Artifact.Type! ),
                                                                   new XAttribute( XmlNames.xName, a.Artifact.Artifact.Name ),
                                                                   new XAttribute( XmlNames.xVersion, a.Artifact.Version.ToNormalizedString() ),
                                                                   new XAttribute( XmlNames.xTarget, a.TargetName ) ) );

            Throw.CheckState( Solutions.All( s => s.CommitSha != null && s.Version.IsValid ) );
            var solutions = Solutions.Select( s => new XElement( XmlNames.xS,
                                                        new XAttribute(XmlNames.xName, s.SolutionName ),
                                                        new XAttribute( XmlNames.xVersion, s.Version.ToNormalizedString() ),
                                                        new XAttribute( XmlNames.xCommitSha, s.CommitSha! ) ) )
                                     .ToArray();

            var releaseNotes = ReleaseNotes != null
                                ? new XElement( nameof( ReleaseNotes ), ReleaseNotes.Select( r => r.ToXml() ) )
                                : null;

            return new XElement( XmlNames.xBuildResult,
                                    new XAttribute( XmlNames.xType, Type.ToString() ),
                                    new XAttribute( XmlNames.xCreationDate, CreationDate ),
                                    WorldReleaseVersion != null ? new XAttribute( XmlNames.xWorldReleaseVersion, WorldReleaseVersion ) : null,
                                    new XAttribute( XmlNames.xVersion, "1" ),
                                    artifacts,
                                    solutions,
                                    releaseNotes );
        }

    }
}
