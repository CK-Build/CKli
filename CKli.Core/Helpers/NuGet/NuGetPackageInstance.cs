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
    readonly ImmutableArray<NuGetPackageInstance> _dependencies;

    /// <summary>
    /// Initializes a new package instance with its dependencies.
    /// </summary>
    /// <param name="packageId">The package name.</param>
    /// <param name="version">The version of this instance.</param>
    /// <param name="dependencies">The dependencies.</param>
    public NuGetPackageInstance( string packageId, SVersion version, ImmutableArray<NuGetPackageInstance> dependencies )
        : base( packageId, version )
    {
        _dependencies = dependencies;
    }

    /// <summary>
    /// Gets this package's dependencies.
    /// </summary>
    public ImmutableArray<NuGetPackageInstance> Dependencies => _dependencies;

    bool IEquatable<NuGetPackageInstance>.Equals( NuGetPackageInstance? other ) => base.Equals( other );
}

