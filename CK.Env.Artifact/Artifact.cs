using System;
using CSemVer;

namespace CK.Env
{
    /// <summary>
    /// An artifact is produced by a solution and can be of any type.
    /// </summary>
    public readonly struct Artifact : IEquatable<Artifact>
    {
        /// <summary>
        /// Gets the artifact type.
        /// </summary>
        public ArtifactType Type { get; }

        /// <summary>
        /// Gets the artifact name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the "Type:Name" string.
        /// This should be used as the identifier for the actual artifact since structurally
        /// nothing prevents a NPM package to be named like a NuGet package or a NuGet package to
        /// also be published as a "Zipped artifact" or any other kind of production.
        /// </summary>
        public string TypedName => $"{Type}:{Name}";

        /// <summary>
        /// Gets whether this Artifact is valid: its type is defined and its
        /// name is not empty.
        /// </summary>
        public bool IsValid => Type.IsValid;

        /// <summary>
        /// Initializes a new <see cref="Artifact"/>.
        /// </summary>
        /// <param name="type">Artifact type. <see cref="ArtifactType.IsValid"/> must be true.</param>
        /// <param name="name">Artifact name. Must not be empty.</param>
        public Artifact( in ArtifactType type, string name )
        {
            if( !type.IsValid ) throw new ArgumentException( "Invalid type.", nameof( type ) );
            if( String.IsNullOrWhiteSpace( name ) ) throw new ArgumentNullException( nameof( name ) );
            Type = type;
            Name = name; 
        }

        /// <summary>
        /// Returns a <see cref="ArtifactInstance"/>.
        /// </summary>
        /// <param name="v">The version.</param>
        /// <returns>The artifact instance.</returns>
        public ArtifactInstance WithVersion( SVersion v ) => new ArtifactInstance( this, v );

        /// <summary>
        /// Checks equality.
        /// </summary>
        /// <param name="other">The other artifact.</param>
        /// <returns>True when equals, false otherwise.</returns>
        public bool Equals( Artifact other ) => Type == other.Type && Name == other.Name;

        /// <summary>
        /// Overridden to call <see cref="Equals(Artifact)"/>.
        /// </summary>
        /// <param name="obj">An object.</param>
        /// <returns>True the object is an Artifact that is equal to this one, false otherwise.</returns>
        public override bool Equals( object obj ) => obj is Artifact a ? Equals( a ) : false;

        /// <summary>
        /// Overrsidden to combine <see cref="Type"/> and <see cref="Name"/>.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => Type.GetHashCode() ^ Name.GetHashCode();

        /// <summary>
        /// Implements == operator.
        /// </summary>
        /// <param name="x">First artifact.</param>
        /// <param name="y">Second artifact.</param>
        /// <returns>True if they are equal.</returns>
        public static bool operator ==( in Artifact x, in Artifact y ) => x.Equals( y );

        /// <summary>
        /// Implements != operator.
        /// </summary>
        /// <param name="x">First artifact.</param>
        /// <param name="y">Second artifact.</param>
        /// <returns>True if they are not equal.</returns>
        public static bool operator !=( in Artifact x, in Artifact y ) => !x.Equals( y );

        /// <summary>
        /// Overridden to return the <see cref="TypedName"/>.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => TypedName;

    }
}
