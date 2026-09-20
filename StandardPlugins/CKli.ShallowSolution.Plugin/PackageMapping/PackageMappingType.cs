namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// How a <see cref="IPackageMapping"/> handles a package identifier: this is what
/// <see cref="IPackageMapping.GetMappingType"/> answers.
/// <para>
/// This exists because a null <see cref="IPackageMapping.GetMappedVersion"/> means two different things. For
/// an exact mapping it is a version that should have been updated and was not - something to report. For a
/// relaxed one it is simply "this reference is where it must be". Only the mapping can tell the two apart, and
/// it can only tell it per identifier: the same composite mapping answers <see cref="Mapped"/> for a package
/// this World produces and <see cref="KnownName"/> for one its bounds merely cover.
/// </para>
/// </summary>
public enum PackageMappingType
{
    /// <summary>
    /// The package identifier is not handled at all: <see cref="IPackageMapping.GetMappedVersion"/> answers null
    /// whatever the version and a reference to it must be left alone. Reading the version is useless.
    /// </summary>
    None,

    /// <summary>
    /// The package identifier is handled, but not necessarily every version of it: a null
    /// <see cref="IPackageMapping.GetMappedVersion"/> means "this version is fine as it is".
    /// <para>
    /// This is what a relaxed mapping answers. The <see cref="PackageBounds"/> of a World map the versions that
    /// are out of the configured bound and only those; a solution whose last build must itself be rebuilt (a
    /// "+fake" or a deprecated version) has no version to offer yet.
    /// </para>
    /// </summary>
    KnownName,

    /// <summary>
    /// The package identifier is handled and so is every version of it: a null
    /// <see cref="IPackageMapping.GetMappedVersion"/> is an anomaly that <see cref="MutableSolution.UpdatePackages"/>
    /// warns about.
    /// <para>
    /// This is what an exact <see cref="PackageMapper"/> answers: "ckli deps update" and the fix workflow compute
    /// the upgrades they want beforehand, so a reference left behind is a reference their update didn't reach.
    /// </para>
    /// </summary>
    Mapped
}
