using CK.Core;
using CSemVer;
using System.Collections.Generic;

namespace CK.Build
{
    /// <summary>
    /// Pure readonly interface that describes the basic information of a package handled by the <see cref="PackageDB"/>
    /// in an independent manner. This is used to import package information in the database.
    /// Once added, <see cref="PackageInstance"/> immutable objects are exposed.
    /// </summary>
    public interface IPackageInfo
    {
        /// <summary>
        /// Gets the key of this package.
        /// </summary>
        ArtifactInstance Key { get; }

        /// <summary>
        /// Gets the savors.
        /// It is null by default can never be <see cref="CKTrait.IsEmpty"/>.
        /// </summary>
        CKTrait? Savors { get; }

        /// <summary>
        /// Gets the set of dependencies.
        /// </summary>
        IEnumerable<(ArtifactInstance Target, SVersionLock Lock, PackageQuality MinQuality, ArtifactDependencyKind Kind, CKTrait? Savors)> Dependencies { get; }

    }
}
