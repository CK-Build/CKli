using CK.Core;
using CK.Env.DependencyModel;
using CSemVer;
using System;

namespace CK.Env
{
    /// <summary>
    /// Captures basic information that describes a package update for a project.
    /// This is simply a <see cref="PackageReference"/> that has been renamed/repackaged in order to be more explicit.
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
        /// Gets the project that must be updated.
        /// This can be null if package must be updated in <see cref="ISolution.SolutionPackageReferences"/>.
        /// </summary>
        public IProject Project => _ref.Referer as IProject;

        /// <summary>
        /// Either the <see cref="IProject.Solution"/>, when <see cref="Project"/> is null,
        /// the solution which <see cref="ISolution.SolutionPackageReferences"/> must be updated.
        /// </summary>
        public ISolution Solution => _ref.Referer.Solution;

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
