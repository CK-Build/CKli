using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    public struct VersionedPackage : IEquatable<VersionedPackage>
    {
        /// <summary>
        /// The package identifier.
        /// </summary>
        public readonly string PackageId;

        /// <summary>
        /// The version of the package.
        /// </summary>
        public readonly SVersion Version;

        /// <summary>
        /// Initializes a new <see cref="VersionedPackage"/>.
        /// </summary>
        /// <param name="p">Package identifier.</param>
        /// <param name="v">Package version.</param>
        public VersionedPackage( string p, SVersion v )
        {
            if( String.IsNullOrWhiteSpace( p ) ) throw new ArgumentNullException( nameof( p ) );
            if( v == null ) throw new ArgumentNullException( nameof( v ) );
            PackageId = p;
            Version = v;
        }

        /// <summary>
        /// Checks whether this <see cref="VersionedPackage"/> is equal to the other one.
        /// </summary>
        /// <param name="other">The </param>
        /// <returns>True if the <see cref="PackageId"/> and <see cref="Version"/> are exactly the same, false otherwise.</returns>
        public bool Equals( VersionedPackage other ) => PackageId == other.PackageId && Version == other.Version;

        /// <summary>
        /// Overridden to call <see cref="Equals(VersionedPackage)"/>.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True if the <see cref="PackageId"/> and <see cref="Version"/> are exactly the same, false otherwise.</returns>
        public override bool Equals( object obj ) => obj is VersionedPackage o ? Equals( o ) : false;

        /// <summary>
        /// Overridden to check equality like <see cref="Equals(VersionedPackage)"/>.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            var hashCode = 1914236829;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode( PackageId );
            hashCode = hashCode * -1521134295 + EqualityComparer<SVersion>.Default.GetHashCode( Version );
            return hashCode;
        }

        /// <summary>
        /// Overridden to return <see cref="PackageId"/>/<see cref="Version"/>.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"{PackageId}/{Version}";

        public static bool operator ==( VersionedPackage package1, VersionedPackage package2 ) => package1.Equals( package2 );

        public static bool operator !=( VersionedPackage package1, VersionedPackage package2 ) => !(package1 == package2);

    }
}
