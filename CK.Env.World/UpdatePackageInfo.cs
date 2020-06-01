using CK.Build;
using CK.Env.DependencyModel;

namespace CK.Env
{
    /// <summary>
    /// Captures basic information that describes a package update for a project or a solution.
    /// This is simply a <see cref="PackageReference"/> that has been renamed/reshaped in order to be more explicit.
    /// </summary>
    public readonly struct UpdatePackageInfo
    {
        readonly PackageReference _ref;

        /// <summary>
        /// Initializes a new <see cref="UpdatePackageInfo"/>.
        /// </summary>
        /// <param name="r">The reference.</param>
        public UpdatePackageInfo( PackageReference r )
        {
            _ref = r;
        }

        /// <summary>
        /// Initializes a new <see cref="PackageReference"/> with a non null referer and valid target.
        /// </summary>
        /// <param name="referer">The referer.</param>
        /// <param name="target">Valid target.</param>
        public UpdatePackageInfo( IPackageReferer referer, ArtifactInstance target )
            : this( new PackageReference( referer, target ) )
        {
        }

        /// <summary>
        /// Gets the project or solution that must be updated.
        /// </summary>
        public IPackageReferer Referer => _ref.Referer;

        /// <summary>
        /// Gets the package to update and its target version.
        /// </summary>
        public ArtifactInstance PackageUpdate => _ref.Target;

        /// <summary>
        /// Overridden to return $"{Referer.Name} <= {PackageUpdate}".
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => _ref.ToString( " <= " );

    }
}
