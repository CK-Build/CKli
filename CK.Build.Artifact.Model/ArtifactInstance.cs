using CSemVer;
using System;

namespace CK.Build
{
    /// <summary>
    /// Defines the instance of an <see cref="Artifact"/>: its <see cref="Version"/> is known.
    /// </summary>
    public readonly struct ArtifactInstance : IEquatable<ArtifactInstance>, IComparable<ArtifactInstance>
    {
        /// <summary>
        /// Initializes a new <see cref="ArtifactInstance"/>.
        /// </summary>
        /// <param name="a">The artifact. Must be <see cref="Artifact.IsValid"/>.</param>
        /// <param name="version">The version. Can not be null and must be <see cref="SVersion.IsValid"/>.</param>
        public ArtifactInstance( Artifact a, SVersion version )
        {
            if( !a.IsValid ) throw new ArgumentException( "Artifact must be valid.", nameof( a ) );
            Artifact = a;
            if( version == null || !version.IsValid ) throw new ArgumentException( "Version must be valid.", nameof( version ) );
            Version = version;
        }

        /// <summary>
        /// Initializes a new <see cref="ArtifactInstance"/>.
        /// </summary>
        /// <param name="type">The artifact type.</param>
        /// <param name="name">The artifact name. Can not be null.</param>
        /// <param name="version">The version. Can not be null.</param>
        public ArtifactInstance( ArtifactType type, string name, SVersion version )
            : this( new Artifact( type, name ), version )
        {
        }

        /// <summary>
        /// Gets the artifact (type and name only).
        /// </summary>
        public Artifact Artifact { get; }

        /// <summary>
        /// Gets the artifact version that is necessarily valid
        /// if <see cref="IsValid"/> is true.
        /// </summary>
        public SVersion Version { get; }

        /// <summary>
        /// Gets whether this instance is valid: both <see cref="Artifact"/> and <see cref="Version"/> are valid.
        /// </summary>
        public bool IsValid => Artifact.IsValid;

        /// <summary>
        /// Compares this instance to another: <see cref="Artifact"/> and descending <see cref="Version"/> are
        /// the order keys.
        /// </summary>
        /// <param name="other">The other instance to compare to. Can be invalid.</param>
        /// <returns>The negative/zero/positive standard value.</returns>
        public int CompareTo( ArtifactInstance other )
        {
            if( !IsValid )
            {
                return other.IsValid ? -1 : 0;
            }
            if( !other.IsValid ) return 1;
            int cmp = Artifact.CompareTo( other.Artifact );
            return cmp != 0 ? cmp : other.Version.CompareTo( Version );
        }

        /// <summary>
        /// Checks equality.
        /// </summary>
        /// <param name="other">The other instance.</param>
        /// <returns>True when equals, false otherwise.</returns>
        public bool Equals( ArtifactInstance other ) => Artifact == other.Artifact && Version == other.Version;

        public override bool Equals( object obj ) => obj is ArtifactInstance a ? Equals( a ) : false;

        public override int GetHashCode() => Version.GetHashCode() ^ Artifact.GetHashCode();

        /// <summary>
        /// Overridden to return <see cref="Artifact"/>/<see cref="Version"/> or the empty string
        /// if <see cref="IsValid"/> is false.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => IsValid ? $"{Artifact}/{Version}" : String.Empty;

        /// <summary>
        /// Simple parse of the "Type:Name/Version" format that may return an invalid
        /// instance (see <see cref="IsValid"/>).
        /// This never throws.
        /// </summary>
        /// <param name="instanceName">The string to parse.</param>
        /// <returns>The resulting instance that may be invalid.</returns>
        public static ArtifactInstance TryParse( string instanceName )
        {
            int idx = instanceName.LastIndexOf( '/' );
            if( idx > 0
                && idx < instanceName.Length - 3
                && SVersion.TryParse( instanceName.Substring( idx + 1 ), out var version )
                && Artifact.TryParse( instanceName.Substring( 0, idx ), out var artifact ) )
            {
                return new ArtifactInstance( artifact, version );
            }
            return new ArtifactInstance();
        }

        /// <summary>
        /// Adapts <see cref="TryParse(string)"/> to the standard pattern. This never throws.
        /// </summary>
        /// <param name="instanceName">The string to parse.</param>
        /// <param name="instance">The resulting instance that may be invalid.</param>
        /// <returns>True on success, false on error.</returns>
        public static bool TryParse( string instanceName, out ArtifactInstance instance )
        {
            instance = TryParse( instanceName );
            return instance.IsValid;
        }

        /// <summary>
        /// Implements == operator.
        /// </summary>
        /// <param name="x">First artifact instance.</param>
        /// <param name="y">Second artifact instance.</param>
        /// <returns>True if they are equal.</returns>
        public static bool operator ==( in ArtifactInstance x, in ArtifactInstance y ) => x.Equals( y );

        /// <summary>
        /// Implements != operator.
        /// </summary>
        /// <param name="x">First artifact instance.</param>
        /// <param name="y">Second artifact instance.</param>
        /// <returns>True if they are not equal.</returns>
        public static bool operator !=( in ArtifactInstance x, in ArtifactInstance y ) => !x.Equals( y );

    }
}
