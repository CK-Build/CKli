using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;

namespace CK.Build.PackageDB
{
    /// <summary>
    /// Simple mutable implementation of <see cref="IPackageInstanceInfo"/>.
    /// </summary>
    public class PackageInstanceInfo : IPackageInstanceInfo
    {
        CKTrait? _savors;

        /// <inheritdoc />
        public ArtifactInstance Key { get; set; }

        /// <summary>
        /// Gets or sets the savors if any.
        /// It is null by default, and note that <see cref="CKTrait.IsEmpty"/> is forbidden and raises an ArgumentException if set.
        /// </summary>
        public CKTrait? Savors
        {
            get => _savors;
            set
            {
                Throw.CheckArgument( "PackageInfo Savors cannot be empty.", value == null || !value.IsEmpty );
                _savors = value;
            }
        }

        /// <summary>
        /// Gets or sets the state of this instance.
        /// </summary>
        public PackageState State { get; set; }

        /// <summary>
        /// Gets the mutable list of dependencies.
        /// This will be transformed into <see cref="PackageInstance.Dependencies"/> that is a set of <see cref="PackageInstance.Reference"/>.
        /// </summary>
        public List<(ArtifactInstance Target, SVersionLock Lock, PackageQuality MinQuality, ArtifactDependencyKind Kind, CKTrait? Savors)> Dependencies { get; } = new List<(ArtifactInstance, SVersionLock, PackageQuality, ArtifactDependencyKind, CKTrait?)>();

        IEnumerable<(ArtifactInstance Target, SVersionLock Lock, PackageQuality MinQuality, ArtifactDependencyKind Kind, CKTrait? Savors)> IPackageInstanceInfo.Dependencies => Dependencies;

    }
}
