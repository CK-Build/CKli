using CK.Core;
using System;
using System.Diagnostics;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Defines a dependency to a package from a generic <see cref="IPackageReferer"/>.
    /// </summary>
    public readonly struct PackageReference
    {
        /// <summary>
        /// Gets the referer of this <see cref="Target"/>.
        /// This is never null and is either a <see cref="ISolution"/> or a <see cref="IProject"/>.
        /// </summary>
        public IPackageReferer Referer { get; }

        /// <summary>
        /// Gets the referenced artifact instance.
        /// It is an installable type (see <see cref="ArtifactType.IsInstallable"/>) and <see cref="ArtifactInstance.IsValid"/>
        /// is necessarily true.
        /// </summary>
        public ArtifactInstance Target { get; }

        /// <summary>
        /// Initializes a new <see cref="PackageReference"/> with a non null referer and valid target.
        /// </summary>
        /// <param name="referer">The referer.</param>
        /// <param name="target">Valid target.</param>
        public PackageReference( IPackageReferer referer, ArtifactInstance target )
        {
            Referer = referer ?? throw new ArgumentNullException(nameof(referer));
            if( !target.IsValid ) throw new ArgumentException( nameof( target ) );
            Target = target;
        }

        /// <summary>
        /// Returns "Referer {link} Target" string.
        /// </summary>
        /// <param name="link">Link between referer and target.</param>
        /// <returns>A readable string.</returns>
        public string ToString( string link ) => Referer.Name + link + Target.ToString();

        /// <summary>
        /// Returns "Referer -> Target" string.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => ToString( " -> " );
    }
}
