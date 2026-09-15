using CK.Core;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Maps a package identifier to the <see cref="SVersionBound"/> its versions must stay in: a version that
/// doesn't <see cref="SVersionBound.Satisfy(in SVersion)"/> its bound is mapped to the bound's
/// <see cref="SVersionBound.Base"/>, a version that satisfies it is left alone.
/// <para>
/// A <see cref="SVersionLock.Lock"/>ed bound accepts its base version only: such a mapper behaves exactly
/// like a <see cref="BrutalPackageMapper"/> on the base version.
/// </para>
/// </summary>
public static class BoundPackageMapper
{
    /// <summary>
    /// Creates a mapper on the World's <see cref="PackageBounds"/>. A bound that a wildcard
    /// <see cref="PackageBounds.Rule"/> carries maps exactly as one declared for the identifier itself: a rule
    /// decides what a bound covers, not what it does.
    /// </summary>
    /// <param name="bounds">The package bounds. When null, <see cref="PackageMapper.Empty"/> is returned.</param>
    /// <returns>A bound mapping.</returns>
    public static IPackageMapping Create( PackageBounds? bounds )
    {
        return bounds != null ? new FromBounds( bounds ) : PackageMapper.Empty;
    }

    sealed class FromBounds : IPackageMapping
    {
        readonly PackageBounds _bounds;

        public FromBounds( PackageBounds bounds ) => _bounds = bounds;

        public bool IsEmpty => _bounds.IsEmpty;

        public bool HasMapping( string packageId ) => _bounds.TryGet( packageId, out _, out _ );

        public SVersion? GetMappedVersion( string packageId, SVersion from )
        {
            return _bounds.TryGet( packageId, out var bound, out _ ) && !bound.Satisfy( from )
                    ? bound.Base
                    : null;
        }

        public override string ToString() => _bounds.ToString();
    }

}
