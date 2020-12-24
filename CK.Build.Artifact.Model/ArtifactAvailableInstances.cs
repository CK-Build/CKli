using CSemVer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CK.Build
{
    /// <summary>
    /// Immutable that defines an Artifact associated to a set of available versions (a <see cref="PackageQualityVector"/>)
    /// in a specific <see cref="Feed"/>.
    /// </summary>
    public class ArtifactAvailableInstances : IEnumerable<ArtifactInstance>
    {
        /// <summary>
        /// Initializes a new <see cref="ArtifactAvailableInstances"/> from an actual feed.
        /// </summary>
        /// <param name="feed">The feed.</param>
        /// <param name="artifactName">The artifact name.</param>
        /// <param name="v">The quality versions.</param>
        public ArtifactAvailableInstances( IArtifactFeedIdentity feed, string artifactName, in PackageQualityVector v = default )
        {
            if( feed == null ) throw new ArgumentNullException( nameof( feed ) );
            Artifact = new Artifact( feed.ArtifactType, artifactName );
            FeedName = feed.Name;
            Versions = v;
        }

        /// <summary>
        /// Initializes a new <see cref="ArtifactAvailableInstances"/>.
        /// </summary>
        /// <param name="feedName">The <see cref="IArtifactFeed.Name"/>.</param>
        /// <param name="artifact">The artifact.</param>
        /// <param name="v">The quality versions.</param>
        public ArtifactAvailableInstances( string feedName, Artifact artifact, in PackageQualityVector v = default )
        {
            if( feedName == null ) throw new ArgumentNullException( nameof( feedName ) );
            Artifact = artifact;
            FeedName = feedName;
            Versions = v;
        }

        /// <summary>
        /// Gets the <see cref="IArtifactFeedIdentity.Name"/> or "*" when multiple available versions
        /// have been combined by <see cref="With(ArtifactAvailableInstances)"/>.
        /// </summary>
        public string FeedName { get; }

        /// <summary>
        /// Gets the artifact type and name.
        /// </summary>
        public Artifact Artifact { get; }

        /// <summary>
        /// Gets the quality versions available for this <see cref="Artifact"/>.
        /// </summary>
        public PackageQualityVector Versions { get; }

        /// <summary>
        /// Returns this <see cref="ArtifactAvailableInstances"/> or a new one that combines a new version.
        /// </summary>
        /// <param name="v">Version to handle. May be null or invalid.</param>
        /// <returns>The new available instances.</returns>
        public ArtifactAvailableInstances WithVersion( SVersion v )
        {
            if( !IsValid || v == null || !v.IsValid ) return this;
            return new ArtifactAvailableInstances( FeedName, Artifact, Versions.WithVersion( v ) );
        }

        /// <summary>
        /// Returns this <see cref="ArtifactAvailableInstances"/> or a new one that combines a new set of available versions.
        /// </summary>
        /// <param name="versions">Versions to combine.</param>
        /// <returns>The new available instances.</returns>
        public ArtifactAvailableInstances WithVersions( PackageQualityVector versions )
        {
            if( !IsValid || !versions.IsValid ) return this;
            return new ArtifactAvailableInstances( FeedName, Artifact, Versions.With( versions ) );
        }

        /// <summary>
        /// Combines this <see cref="ArtifactAvailableInstances"/> with another one.
        /// Both <see cref="Artifact"/> must be equal otherwise an <see cref="ArgumentException"/> is thrown.
        /// If <see cref="FeedName"/> differ, the resulting FeedName is "*".
        /// </summary>
        /// <param name="other">Other available instances to combine.</param>
        /// <returns>The new available instances.</returns>
        public ArtifactAvailableInstances With( ArtifactAvailableInstances other )
        {
            if( other == null ) throw new ArgumentNullException( nameof( other ) );
            if( !other.IsValid ) return this;
            if( !IsValid ) return other;
            if( Artifact != other.Artifact ) throw new ArgumentException( $"Cannot combine versions for '{Artifact}' with '{other.Artifact}'.", nameof( other ) );
            var name = FeedName == other.FeedName ? FeedName : "*";
            return new ArtifactAvailableInstances( name, Artifact, other.Versions );
        }

        /// <summary>
        /// Gets whether the <see cref="Artifact"/> is valid.
        /// This artifact type and nam may be valid but there may be no available
        /// versions (<see cref="PackageQualityVersions.IsValid"/> may be false).
        /// </summary>
        public bool IsValid => Artifact.IsValid;

        /// <summary>
        /// Gets the best stable version or an invalid instance (<see cref="ArtifactInstance.IsValid"/> is false) if
        /// no such version exist.
        /// </summary>
        public ArtifactInstance Stable => Versions.Stable != null ? new ArtifactInstance( Artifact, Versions.Stable ) : new ArtifactInstance();

        /// <summary>
        /// Gets the best release candidate version or an invalid instance (<see cref="ArtifactInstance.IsValid"/> is false) if
        /// no such version exist.
        /// </summary>
        public ArtifactInstance Latest => Versions.ReleaseCandidate != null ? new ArtifactInstance( Artifact, Versions.ReleaseCandidate ) : new ArtifactInstance();

        /// <summary>
        /// Gets the best preview compatible version or an invalid instance (<see cref="ArtifactInstance.IsValid"/> is false) if
        /// no such version exist.
        public ArtifactInstance Preview => Versions.Preview != null ? new ArtifactInstance( Artifact, Versions.Preview ) : new ArtifactInstance();

        /// <summary>
        /// Gets the best exploratory compatible version or an invalid instance (<see cref="ArtifactInstance.IsValid"/> is false) if
        /// no such version exist.
        public ArtifactInstance Exloratory => Versions.Exploratory != null ? new ArtifactInstance( Artifact, Versions.Exploratory ) : new ArtifactInstance();

        /// <summary>
        /// Gets the best CI version or an invalid instance (<see cref="ArtifactInstance.IsValid"/> is false) if no such version exist.
        public ArtifactInstance CI => Versions.CI != null ? new ArtifactInstance( Artifact, Versions.CI ) : new ArtifactInstance();

        /// <summary>
        /// Returns the "<see cref="Artifact"/> (<see cref="Versions"/>)" if this artifact is valid.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => IsValid ? $"{Artifact} ({Versions})" : String.Empty;

        /// <summary>
        /// Gets the set of instances of this <see cref="Artifact"/> with its <see cref="Versions"/>.
        /// Instances are CI, Exploratory, Preview, ReleaseCandidate, Stable (in this order and if they exist).
        /// </summary>
        /// <returns>The available instances.</returns>
        public IEnumerator<ArtifactInstance> GetEnumerator() => Versions.Select( v => new ArtifactInstance( Artifact, v ) ).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

}
