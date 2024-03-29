using CK.Core;
using CSemVer;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CK.Build
{
    /// <summary>
    /// An artifact is produced by a solution and can be of any type.
    /// Implements value equality and comparison based on <see cref="Type"/> that use <see cref="StringComparer.Ordinal"/>
    /// and the <see cref="Name"/> with <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    public readonly struct Artifact : IEquatable<Artifact>, IComparable<Artifact>
    {
        /// <summary>
        /// Gets the artifact type.
        /// This is null when <see cref="IsValid"/> is false.
        /// </summary>
        public ArtifactType? Type { get; }

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
        /// Gets whether this Artifact is valid: its type is not null and its
        /// name is not empty.
        /// </summary>
        [MemberNotNullWhen(true,nameof(Type))]
        public bool IsValid => Type != null;

        /// <summary>
        /// Initializes a new <see cref="Artifact"/>.
        /// </summary>
        /// <param name="type">Artifact type. <see cref="ArtifactType.IsValid"/> must be true.</param>
        /// <param name="name">Artifact name. Must not be null or whitespace.</param>
        public Artifact( in ArtifactType type, string name )
        {
            Throw.CheckNotNullArgument( type );
            Throw.CheckNotNullOrWhiteSpaceArgument( name );
            Type = type;
            Name = name;
        }

        /// <summary>
        /// Simple parse of the "Type:Name" format that may return an invalid
        /// type (see <see cref="IsValid"/>).
        /// This never throws.
        /// </summary>
        /// <param name="typedName">The string to parse.</param>
        /// <returns>The resulting artifact that may be invalid.</returns>
        public static Artifact TryParse( string? typedName )
        {
            if( !String.IsNullOrWhiteSpace( typedName ) )
            {
                var idx = typedName.IndexOf( ':' );
                if( idx > 0 && idx < typedName.Length - 1 )
                {
                    var type = ArtifactType.SingleOrDefault( typedName.Substring( 0, idx ) );
                    if( type != null )
                    {
                        return new Artifact( type, typedName.Substring( idx + 1 ) );
                    }
                }
            }
            return new Artifact();
        }


        /// <summary>
        /// Adapts <see cref="TryParse(string)"/> to the standard pattern.
        /// </summary>
        /// <param name="typedName">The string to parse.</param>
        /// <param name="artifact">The resulting artifact that may be invalid.</param>
        /// <returns>True on success, false on error.</returns>
        public static bool TryParse( string typedName, out Artifact artifact )
        {
            artifact = TryParse( typedName );
            return artifact.IsValid;
        }

        /// <summary>
        /// Tries to parse the <paramref name="nameOrTypedName"/> and on failure use the
        /// fallback type to create a new named artifact.
        /// This never throws.
        /// </summary>
        /// <param name="nameOrTypedName">The valid typed name or the future name of the artifact.</param>
        /// <param name="fallbackType">The fallback type. When null and try parse failed, an invalid artifact is returned.</param>
        /// <returns>The resulting artifact that may be invalid.</returns>
        public static Artifact TryParseOrCreate( string? nameOrTypedName, ArtifactType? fallbackType )
        {
            if( !String.IsNullOrWhiteSpace( nameOrTypedName ) )
            {
                var name = Artifact.TryParse( nameOrTypedName );
                if( !name.IsValid && fallbackType != null ) name = new Artifact( fallbackType, nameOrTypedName );
                return name;
            }
            return new Artifact();
        }

        /// <summary>
        /// Returns a <see cref="ArtifactInstance"/>.
        /// </summary>
        /// <param name="v">The version.</param>
        /// <returns>The artifact instance.</returns>
        public ArtifactInstance WithVersion( SVersion v ) => new ArtifactInstance( this, v );

        /// <summary>
        /// Returns a <see cref="ArtifactBound"/>.
        /// </summary>
        /// <param name="b">The version bound.</param>
        /// <returns>The artifact bound.</returns>
        public ArtifactBound WithBound( SVersionBound b ) => new ArtifactBound( this, b );

        /// <summary>
        /// Checks equality.
        /// </summary>
        /// <param name="other">The other artifact.</param>
        /// <returns>True when equals, false otherwise.</returns>
        public bool Equals( Artifact other ) => Type == other.Type && Name.Equals( other.Name, StringComparison.OrdinalIgnoreCase );

        /// <summary>
        /// Overridden to call <see cref="Equals(Artifact)"/>.
        /// </summary>
        /// <param name="obj">An object.</param>
        /// <returns>True the object is an Artifact that is equal to this one, false otherwise.</returns>
        public override bool Equals( object? obj ) => obj is Artifact a && Equals( a );

        /// <summary>
        /// Overridden to combine <see cref="Type"/> and <see cref="Name"/>.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => (Type?.GetHashCode() ?? 0) ^ StringComparer.OrdinalIgnoreCase.GetHashCode( Name );

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

        /// <summary>
        /// Compares this artifact to another: <see cref="Type"/> and <see cref="Name"/> are the order keys.
        /// </summary>
        /// <param name="other">The other artifact to compare to. Can be invalid.</param>
        /// <returns>The negative/zero/positive standard value.</returns>
        public int CompareTo( Artifact other )
        {
            if( !IsValid )
            {
                return other.IsValid ? -1 : 0;
            }
            if( !other.IsValid ) return 1;
            Debug.Assert( Type != null && other.Type != null );
            int cmp = Type.CompareTo( other.Type );
            return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare( Name, other.Name );
        }
    }
}
