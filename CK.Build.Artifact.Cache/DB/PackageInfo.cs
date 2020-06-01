using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.Build
{
    /// <summary>
    /// Simple mutable implementation of <see cref="IPackageInfo"/>.
    /// </summary>
    public class PackageInfo : IPackageInfo
    {
        CKTrait? _savors;

        /// <summary>
        /// Gets or sets the key of this package.
        /// </summary>
        public ArtifactInstance Key { get; set; }

        /// <summary>
        /// Gets or sets the savors if any.
        /// It is null by default, and note that <see cref="CKTrait.IsEmpty"/> is forbidden and raises an ArgumentExceptiuon if set.
        /// </summary>
        public CKTrait? Savors
        {
            get => _savors;
            set
            {
                if( value != null && value.IsEmpty ) throw new ArgumentException( "PackageInfo Savors cannot be empty.", "value" );
                _savors = value;
            }
        }

        /// <summary>
        /// Gets the mutable list of dependencies.
        /// </summary>
        public List<(ArtifactInstance Target, ArtifactDependencyKind Kind, CKTrait? Savors)> Dependencies { get; } = new List<(ArtifactInstance, ArtifactDependencyKind, CKTrait?)>();

        IEnumerable<(ArtifactInstance Target, ArtifactDependencyKind Kind, CKTrait? Savors)> IPackageInfo.Dependencies => Dependencies;

    }
}
