using CSemVer;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Defines an Artifact associated to a <see cref="PackageQualityVersions"/>.
    /// </summary>
    public readonly struct ArtifactAvailableInstances
    {
        /// <summary>
        /// Initializes a new <see cref="ArtifactAvailableInstances"/>.
        /// </summary>
        /// <param name="a">The artifact.</param>
        /// <param name="v">The quality versions.</param>
        public ArtifactAvailableInstances( Artifact a, PackageQualityVersions v )
        {
            if( !a.IsValid ) throw new ArgumentException();
            Artifact = a;
            Versions = v;
        }

        /// <summary>
        /// Initializes a new <see cref="ArtifactAvailableInstances"/> with an initially invalid <see cref="Versions"/>.
        /// </summary>
        /// <param name="a">The artifact.</param>
        public ArtifactAvailableInstances( Artifact a )
            : this( a, new PackageQualityVersions() )
        {
        }

        /// <summary>
        /// Gets the artifact.
        /// </summary>
        public Artifact Artifact { get; }

        /// <summary>
        /// Gets the quality versions available for this <see cref="Artifact"/>.
        /// </summary>
        public PackageQualityVersions Versions { get; }

        /// <summary>
        /// Retuns this <see cref="ArtifactAvailableInstances"/> or a new one that combines a new version.
        /// </summary>
        /// <param name="v">Version to handle. May be null or invalid.</param>
        /// <returns>The new available instances.</returns>
        public ArtifactAvailableInstances WithVersion( SVersion v )
        {
            if( !IsValid || v == null || !v.IsValid ) return this;
            return new ArtifactAvailableInstances( Artifact, Versions.WithVersion( v ) );
        }

        /// <summary>
        /// Gets whether the <see cref="Artifact"/> is valid.
        /// There may be no available versions (<see cref="PackageQualityVersions.IsValid"/> may be false).
        /// </summary>
        public bool IsValid => Artifact.IsValid;

        /// <summary>
        /// Gets the best stable version or an invalid instance (<see cref="ArtifactInstance.IsValid"/> is false) if
        /// no such version exist .
        /// </summary>
        public ArtifactInstance BestStable => Versions.BestStable != null ? new ArtifactInstance( Artifact, Versions.BestStable ) : new ArtifactInstance();

        /// <summary>
        /// Gets the best latest compatible version or an invalid instance (<see cref="ArtifactInstance.IsValid"/> is false) if
        /// no such version exist .
        /// </summary>
        public ArtifactInstance BestLatest => Versions.BestLatest != null ? new ArtifactInstance( Artifact, Versions.BestLatest ) : new ArtifactInstance();

        /// <summary>
        /// Gets the best preview compatible version or an invalid instance (<see cref="ArtifactInstance.IsValid"/> is false) if
        /// no such version exist .
        public ArtifactInstance BestPreview => Versions.BestPreview != null ? new ArtifactInstance( Artifact, Versions.BestPreview ) : new ArtifactInstance();

        /// <summary>
        /// Gets the best exploratory compatible version or an invalid instance (<see cref="ArtifactInstance.IsValid"/> is false) if
        /// no such version exist .
        public ArtifactInstance BestExloratory => Versions.BestExploratory != null ? new ArtifactInstance( Artifact, Versions.BestExploratory ) : new ArtifactInstance();

        /// <summary>
        /// Returns the "<see cref="Artifact"/> (<see cref="Versions"/>)" if this artifact is valid.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => IsValid ? $"{Artifact} ({Versions})" : String.Empty;

    }

}
