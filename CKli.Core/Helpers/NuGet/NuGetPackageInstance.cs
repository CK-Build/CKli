using CK.Core;
using System;
using System.Collections.Immutable;

namespace CKli;

// The IEquatable<NuGetPackageInstance>.Equals( NuGetPackageInstance? other ) is explicitly implemented:
// the base equals does the job.
#pragma warning disable CA1067 // Override Object.Equals(object) when implementing IEquatable<T>

/// <summary>
/// Extends <see cref="PackageInstance"/> to capture its <see cref="Dependencies"/>.
/// Exposed by <see cref="Core.NuGetDependencyCache"/>.
/// </summary>
public sealed class NuGetPackageInstance : PackageInstance, IEquatable<NuGetPackageInstance>
{
    readonly ImmutableArray<Dependency> _dependencies;

    /// <summary>
    /// A dependency of a <see cref="NuGetPackageInstance"/>.
    /// <para>
    /// A nuspec declares its dependencies per target framework, so the same <paramref name="Package"/>
    /// can appear under more than one of them - and the same package identifier can appear in two
    /// different versions. The <paramref name="TargetFramework"/> is what qualifies such an edge:
    /// resolving a dependency graph for a given framework requires it.
    /// </para>
    /// </summary>
    /// <param name="TargetFramework">
    /// The target framework of the nuspec's dependency group, as it appears in the file (".NETStandard2.0",
    /// "net10.0", ...). The empty string when the dependency applies to any framework: a flat dependency
    /// list or a &lt;group&gt; without a "targetFramework" attribute.
    /// </param>
    /// <param name="Package">The required package.</param>
    public readonly record struct Dependency( string TargetFramework, NuGetPackageInstance Package );

    /// <summary>
    /// Initializes a new package instance with its dependencies.
    /// </summary>
    /// <param name="packageId">The package name.</param>
    /// <param name="version">The version of this instance.</param>
    /// <param name="dependencies">The dependencies.</param>
    public NuGetPackageInstance( string packageId, SVersion version, ImmutableArray<Dependency> dependencies )
        : base( packageId, version )
    {
        _dependencies = dependencies;
    }

    /// <summary>
    /// Gets this package's dependencies. A (<see cref="Dependency.TargetFramework"/>, package identifier,
    /// version) triple appears at most once.
    /// </summary>
    public ImmutableArray<Dependency> Dependencies => _dependencies;

    bool IEquatable<NuGetPackageInstance>.Equals( NuGetPackageInstance? other ) => base.Equals( other );
}
