using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CKli.Publish.Plugin;

/// <summary>
/// The set of packages a World offers on a branch: exactly one version per package identifier, and every version
/// required by one of them is the one this profile offers.
/// <para>
/// Built by <see cref="PublishedProfileBuilder.BuildFinalProfile"/> from the real content of the builds and of the
/// version tags. A profile always exists in a coherent state: the builder fails rather than producing one that
/// offers two versions of a package identifier.
/// </para>
/// </summary>
sealed class PublishedProfile
{
    readonly Dictionary<string, PublishedPackageInfo> _packages;

    PublishedProfile( Dictionary<string, PublishedPackageInfo> packages )
    {
        _packages = packages;
    }

    /// <summary>
    /// Gets the offered packages, ordered by <see cref="PackageInstance.PackageId"/>.
    /// </summary>
    public IEnumerable<PublishedPackageInfo> Packages => _packages.Values.OrderBy( p => p.PackageId, StringComparer.OrdinalIgnoreCase );

    /// <summary>
    /// Gets the number of offered packages.
    /// </summary>
    public int Count => _packages.Count;

    /// <summary>
    /// Gets the version this profile offers for a package identifier.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="package">The offered package on success.</param>
    /// <returns>True if this profile offers the package identifier, false otherwise.</returns>
    public bool TryGet( string packageId, [NotNullWhen( true )] out PublishedPackageInfo? package )
    {
        return _packages.TryGetValue( packageId, out package );
    }

    /// <summary>
    /// Accumulates the package identifiers a publication offers and the versions its packages require, detecting any
    /// package identifier that would be offered in more than one version.
    /// </summary>
    internal sealed class Builder
    {
        readonly Dictionary<string, PublishedPackageInfo> _packages;
        int _conflictCount;

        public Builder()
        {
            _packages = new Dictionary<string, PublishedPackageInfo>( StringComparer.OrdinalIgnoreCase );
        }

        /// <summary>
        /// Adds a version for a package identifier. The first one registered is the offered one; a different one is
        /// recorded as a conflict on the existing <see cref="PublishedPackageInfo"/> and logged.
        /// </summary>
        public void Add( IActivityMonitor monitor, string packageId, SVersion version, PublishedPackageInfo.Reason reason )
        {
            if( _packages.TryGetValue( packageId, out var already ) )
            {
                if( !already.Add( version, reason ) )
                {
                    ++_conflictCount;
                    monitor.Error( $"'{packageId}' is offered in '{already.Version}' and in '{version}' ({reason})." );
                }
                return;
            }
            _packages.Add( packageId, new PublishedPackageInfo( packageId, version, reason ) );
        }

        /// <summary>
        /// Gets the profile or null when any package identifier has been added in more than one version.
        /// </summary>
        public PublishedProfile? Build( IActivityMonitor monitor )
        {
            if( _conflictCount > 0 )
            {
                monitor.Error( $"{_conflictCount} package version conflict(s): the publication would offer an incoherent profile." );
                return null;
            }
            monitor.Trace( $"Published profile of {_packages.Count} packages." );
            return new PublishedProfile( _packages );
        }
    }
}
