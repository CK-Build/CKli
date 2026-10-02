using CK.Core;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Decides the version of a package that the two sides of a merge reference differently: see
/// <see cref="PackageVersionMerge"/>.
/// </summary>
public interface IPackageVersionResolver
{
    /// <summary>
    /// Gets the version that both sides must reference.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="ours">The version referenced by the branch that receives the merge.</param>
    /// <param name="theirs">The version referenced by the merged commit.</param>
    /// <returns>The version to reference.</returns>
    SVersion Resolve( string packageId, SVersion ours, SVersion theirs );
}
