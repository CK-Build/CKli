using CSemVer;
using System;
using System.Collections.Immutable;

namespace CKli;

public partial class PackageInstance
{
    /// <summary>
    /// Extends <see cref="PackageInstance"/> to capture its <see cref="Dependencies"/>.
    /// Exposed by <see cref="Core.NuGetDependencyCache"/>.
    /// </summary>
    public sealed class WithDependencies : PackageInstance, IEquatable<WithDependencies>
    {
        readonly ImmutableArray<WithDependencies> _dependencies;

        /// <summary>
        /// Initializes a new package instance with its dependencies.
        /// </summary>
        /// <param name="packageId">The package name.</param>
        /// <param name="version">The version of this instance.</param>
        /// <param name="dependencies">The dependencies.</param>
        public WithDependencies( string packageId, SVersion version, ImmutableArray<WithDependencies> dependencies )
            : base( packageId, version )
        {
            _dependencies = dependencies;
        }

        /// <summary>
        /// Gets this package's dependencies.
        /// </summary>
        public ImmutableArray<WithDependencies> Dependencies => _dependencies;

        bool IEquatable<WithDependencies>.Equals( WithDependencies? other ) => base.Equals( other );
    }

}
