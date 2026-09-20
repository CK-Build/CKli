
using CK.Core;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Primary package mapping interface.
/// <para>
/// This mapping handles more than one version mapping for a package identifier. This supports
/// an aggressive programming model in which only the exact dependencies version are updated
/// (the default <see cref="PackageMapper"/> implements this).
/// </para>
/// <para>
/// More relaxed implementations are possible. See <see cref="BrutalPackageMapper"/> for fully
/// relaxed mappers.
/// </para>
/// <para>
/// A mapping says which of the two it is, per package identifier: see <see cref="PackageMappingType"/>.
/// </para>
/// </summary>
public interface IPackageMapping
{
    /// <summary>
    /// Gets whether there is at least one mapping.
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Gets how this mapping handles a package identifier: whether it handles it at all, and whether a null
    /// <see cref="GetMappedVersion"/> for it is "nothing to change" or a version that should have been mapped.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <returns>The way this mapping handles the package identifier.</returns>
    PackageMappingType GetMappingType( string packageId );

    /// <summary>
    /// Gets the mapped version.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="from">The origin version.</param>
    /// <returns>
    /// Null when this version must not be changed. Whether that is normal is what
    /// <see cref="GetMappingType"/> tells: it is for a <see cref="PackageMappingType.KnownName"/> package,
    /// it is not for a <see cref="PackageMappingType.Mapped"/> one.
    /// </returns>
    SVersion? GetMappedVersion( string packageId, SVersion from );
}
