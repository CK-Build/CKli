using CK.Core;
using CK.Packaging.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CKli.Core;

/// <summary>
/// Computes the <see cref="TransitiveDependencies"/> of a set of root packages: what a restore of these
/// roots brings beyond the roots themselves.
/// <para>
/// This is a traversal and a classification, not a resolution. <see cref="NuGetDependencyCache.Get"/> is
/// eagerly recursive, so obtaining a root materializes its whole sub graph: the walk below only has to
/// collect the requirements and decide, identifier by identifier, which version the profile resolves to.
/// </para>
/// </summary>
public static class NuGetDependencyClosure
{
    /// <summary>
    /// Selects the dependencies of a <paramref name="package"/> that the closure must follow.
    /// <para>
    /// This is deliberately not a per edge predicate. For a target framework T, NuGet picks the single
    /// best matching dependency group of a package - a "net9.0" group beats a ".NETStandard2.0" one for a
    /// net10.0 consumer - so choosing requires all of that package's groups at once. Which edges are
    /// followed therefore depends on T, and the closure's member set is a function of T: a per target
    /// closure is not a subset of the unfiltered union.
    /// </para>
    /// </summary>
    /// <param name="package">The package whose dependencies are being followed.</param>
    /// <param name="dependencies">
    /// The <see cref="NuGetPackageInstance.Dependencies"/> of the <paramref name="package"/>: every group
    /// of its nuspec, each edge carrying the <see cref="NuGetPackageInstance.Dependency.TargetFramework"/>
    /// it comes from.
    /// </param>
    /// <returns>The dependencies to follow. Must be a subset of the <paramref name="dependencies"/>.</returns>
    public delegate ImmutableArray<NuGetPackageInstance.Dependency> FrameworkSelector(
                        NuGetPackageInstance package,
                        ImmutableArray<NuGetPackageInstance.Dependency> dependencies );

    /// <summary>
    /// The <see cref="FrameworkSelector"/> that follows every dependency of every group. This is the only
    /// framework blind closure and the one CKli computes today: stacks do not multi target.
    /// </summary>
    public static readonly FrameworkSelector AnyFramework = static ( package, dependencies ) => dependencies;

    /// <summary>
    /// Computes the closure of the <paramref name="directDependencies"/>.
    /// <para>
    /// An identifier of the closure is classified by what anchors its version: a
    /// <paramref name="producedPackages"/> entry, then a <paramref name="directDependencies"/> entry, then
    /// its own requirements. For the two anchored cases only the harmful direction is reported - a
    /// requirement below what the profile references or produces is invisible to a restore - so an
    /// identifier whose requirements are all satisfied by its anchor is dropped from the closure entirely.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="cache">
    /// The cache to read the nuspec files from. Its <see cref="NuGetDependencyCache.Missing"/> and
    /// <see cref="NuGetDependencyCache.MissingLinks"/> detail what could not be read.
    /// </param>
    /// <param name="directDependencies">
    /// The roots: the packages consumed by at least one repository, minus the produced ones. No identifier
    /// can appear twice, nor be one of the <paramref name="producedPackages"/>.
    /// </param>
    /// <param name="producedPackages">
    /// The packages the profile produces. No identifier can appear twice.
    /// </param>
    /// <param name="frameworkSelector">
    /// Selects the edges to follow. Defaults to <see cref="AnyFramework"/>.
    /// </param>
    /// <returns>The closure or null on error.</returns>
    public static TransitiveDependencies? Create( IActivityMonitor monitor,
                                                  NuGetDependencyCache cache,
                                                  IEnumerable<PackageInstance> directDependencies,
                                                  IEnumerable<PackageInstance> producedPackages,
                                                  FrameworkSelector? frameworkSelector = null )
    {
        ArgumentNullException.ThrowIfNull( monitor );
        ArgumentNullException.ThrowIfNull( cache );
        ArgumentNullException.ThrowIfNull( directDependencies );
        ArgumentNullException.ThrowIfNull( producedPackages );
        frameworkSelector ??= AnyFramework;
        if( !CreateIndex( monitor, directDependencies, "direct dependency", out var roots )
            || !CreateIndex( monitor, producedPackages, "produced package", out var produced ) )
        {
            return null;
        }
        foreach( var root in roots.Values )
        {
            if( produced.TryGetValue( root.PackageId, out var conflict ) )
            {
                monitor.Error( $"Direct dependency '{root}' is also produced ('{conflict}'): the direct "
                               + "dependencies are the consumed packages minus the produced ones." );
                return null;
            }
        }
        // The requirements, by package identifier (case insensitive). A version can be required by several
        // packages, under several target frameworks.
        var requirements = new Dictionary<string, Requirement>( StringComparer.OrdinalIgnoreCase );
        // PackageInstance equality is the case insensitive identifier and the version: this guards the
        // walk against re-entering a sub graph that a diamond makes reachable twice.
        var visited = new HashSet<PackageInstance>();
        // The instances that could not be read AND whose absence hides a part of the closure. Which ones
        // those are is only known once each identifier has been classified: see ResolvedMissing below.
        var missing = new HashSet<PackageInstance>();
        foreach( var root in roots.Values )
        {
            if( !cache.Get( monitor, root.PackageId, root.Version, out var package ) )
            {
                return null;
            }
            if( package == null )
            {
                // A root that is absent from the cache is missing - its own dependencies are unknown - but
                // it is not a member of the closure: the direct dependencies already carry it.
                missing.Add( root );
                continue;
            }
            if( visited.Add( package ) )
            {
                Walk( cache, package, frameworkSelector, requirements, visited );
            }
        }
        var regular = ImmutableArray.CreateBuilder<PackageInstance>();
        var ambiguous = ImmutableArray.CreateBuilder<AmbiguousDependency>();
        foreach( var r in requirements.Values )
        {
            if( produced.TryGetValue( r.PackageId, out var anchor ) )
            {
                // The profile produces this identifier itself: a restore gets the produced package, whatever
                // the closure asks for. Nothing of it can be missing.
                AddAnchored( ambiguous, r, anchor, VersionSource.ProducedPackages );
            }
            else if( roots.TryGetValue( r.PackageId, out anchor ) )
            {
                // A repository references this identifier explicitly, so NuGet's nearest-wins makes that
                // version authoritative. The root has been walked above: its absence is already recorded.
                AddAnchored( ambiguous, r, anchor, VersionSource.DirectDependencies );
            }
            else if( r.Count == 1 )
            {
                var resolved = r.Resolved;
                regular.Add( resolved );
                if( r.IsMissing( resolved.Version ) ) missing.Add( resolved );
            }
            else
            {
                // Nothing outside the closure anchors this identifier: NuGet's highest-wins applies and
                // every requirement is a disagreement with one of the others.
                var resolved = r.Resolved;
                ambiguous.Add( new AmbiguousDependency( r.PackageId,
                                                        resolved.Version,
                                                        VersionSource.TransitiveDependencies,
                                                        r.GetRequirements( null ) ) );
                if( r.IsMissing( resolved.Version ) ) missing.Add( resolved );
            }
        }
        if( missing.Count > 0 )
        {
            monitor.Warn( $"""
                          {missing.Count} package(s) required by the profile are absent from the NuGet cache (path: {cache.GetCachePath( monitor )}).
                          Their own dependencies are unknown, so the transitive closure is incomplete:
                          {missing.Order().Select( p => p.ToString() ).Concatenate( Environment.NewLine )}
                          """ );
        }
        return new TransitiveDependencies( regular.DrainToImmutable(),
                                           ambiguous.DrainToImmutable(),
                                           [.. missing] );
    }

    // Indexes the packages by identifier (case insensitive) and checks that no identifier appears twice.
    static bool CreateIndex( IActivityMonitor monitor,
                             IEnumerable<PackageInstance> packages,
                             string what,
                             out Dictionary<string, PackageInstance> index )
    {
        index = new Dictionary<string, PackageInstance>( StringComparer.OrdinalIgnoreCase );
        foreach( var p in packages )
        {
            ArgumentNullException.ThrowIfNull( p, nameof( packages ) );
            if( index.TryGetValue( p.PackageId, out var already ) )
            {
                monitor.Error( $"Incoherent {what} '{p.PackageId}': it appears as '{already.Version}' and "
                               + $"'{p.Version}'. One identifier can only be at one version." );
                return false;
            }
            index.Add( p.PackageId, p );
        }
        return true;
    }

    // Collects the requirements of the sub graph rooted on the package. No nuspec is read here: the cache
    // materialized the whole sub graph when the root was obtained.
    static void Walk( NuGetDependencyCache cache,
                      NuGetPackageInstance root,
                      FrameworkSelector frameworkSelector,
                      Dictionary<string, Requirement> requirements,
                      HashSet<PackageInstance> visited )
    {
        var stack = new Stack<NuGetPackageInstance>();
        stack.Push( root );
        while( stack.TryPop( out var package ) )
        {
            foreach( var (targetFramework, dependency) in frameworkSelector( package, package.Dependencies ) )
            {
                if( !requirements.TryGetValue( dependency.PackageId, out var r ) )
                {
                    r = new Requirement( dependency.PackageId );
                    requirements.Add( dependency.PackageId, r );
                }
                r.Add( dependency, package, targetFramework, cache.Missing.Contains( dependency ) );
                // A missing dependency has no dependency of its own: pushing it is harmless.
                if( visited.Add( dependency ) )
                {
                    stack.Push( dependency );
                }
            }
        }
    }

    // Adds the ambiguity of an identifier that an anchor resolves, or nothing at all when every
    // requirement is satisfied by the anchor.
    static void AddAnchored( ImmutableArray<AmbiguousDependency>.Builder ambiguous,
                             Requirement r,
                             PackageInstance anchor,
                             VersionSource resolvedFrom )
    {
        // A requirement equal to the anchor is not a disagreement, and one below it is invisible to a
        // restore: reporting it would bury the list, since "an identifier is both anchored and transitively
        // required" is the common case.
        var kept = r.GetRequirements( anchor.Version );
        if( kept.Length > 0 )
        {
            ambiguous.Add( new AmbiguousDependency( anchor.PackageId, anchor.Version, resolvedFrom, kept ) );
        }
    }

    // The versions of one package identifier that the closure requires, with the packages that require
    // each of them and the target frameworks under which they do.
    sealed class Requirement
    {
        readonly string _packageId;
        readonly Dictionary<SVersion, Detail> _byVersion;
        PackageInstance? _resolved;

        sealed class Detail
        {
            public readonly PackageInstance Instance;
            public readonly HashSet<PackageInstance> RequiredBy;
            public readonly HashSet<string> TargetFrameworks;
            public bool IsMissing;

            // The cache's instances are used as they are: a NuGetPackageInstance IS an immutable
            // PackageInstance, and it inherits the ToString() that the profile serializes.
            public Detail( PackageInstance instance )
            {
                Instance = instance;
                RequiredBy = new HashSet<PackageInstance>();
                TargetFrameworks = new HashSet<string>( StringComparer.Ordinal );
            }
        }

        public Requirement( string packageId )
        {
            _packageId = packageId;
            _byVersion = new Dictionary<SVersion, Detail>();
        }

        /// <summary>
        /// Gets the package identifier, as the nuspec of the first edge that required it spells it.
        /// </summary>
        public string PackageId => _packageId;

        /// <summary>
        /// Gets the number of distinct required versions. Always at least 1.
        /// </summary>
        public int Count => _byVersion.Count;

        /// <summary>
        /// Gets the greatest required version: what NuGet's highest-wins resolves this identifier to when
        /// nothing outside the closure anchors it.
        /// </summary>
        public PackageInstance Resolved => _resolved ??= _byVersion.Values.Max( d => d.Instance )!;

        public void Add( PackageInstance instance, PackageInstance requiredBy, string targetFramework, bool isMissing )
        {
            if( !_byVersion.TryGetValue( instance.Version, out var d ) )
            {
                d = new Detail( instance );
                _byVersion.Add( instance.Version, d );
                _resolved = null;
            }
            d.RequiredBy.Add( requiredBy );
            d.TargetFrameworks.Add( targetFramework );
            d.IsMissing |= isMissing;
        }

        /// <summary>
        /// Gets whether the given required version could not be read from the cache.
        /// </summary>
        /// <param name="version">The required version.</param>
        /// <returns>True if the version is missing, false otherwise.</returns>
        public bool IsMissing( SVersion version ) => _byVersion[version].IsMissing;

        /// <summary>
        /// Gets the requirements, optionally only the ones above an anchor version.
        /// </summary>
        /// <param name="above">
        /// When not null, only the requirements strictly greater than this version are returned.
        /// </param>
        /// <returns>The requirements. Sorting is the <see cref="AmbiguousDependency"/>'s business.</returns>
        public ImmutableArray<VersionRequirement> GetRequirements( SVersion? above )
        {
            var b = ImmutableArray.CreateBuilder<VersionRequirement>( _byVersion.Count );
            foreach( var (version, d) in _byVersion )
            {
                if( above != null && version <= above ) continue;
                b.Add( new VersionRequirement( version, [.. d.RequiredBy], [.. d.TargetFrameworks] ) );
            }
            return b.DrainToImmutable();
        }
    }
}
