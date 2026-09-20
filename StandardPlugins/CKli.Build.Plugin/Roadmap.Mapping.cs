using CK.Core;
using CKli.HotZone.Plugin;
using CKli.ShallowSolution.Plugin;

using System.Collections.Immutable;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    sealed class Mapping : IPackageMapping
    {
        readonly HotGraph.PackageUpdater _packageUpdater;
        readonly ImmutableArray<BuildSolution> _orderedSolutions;
        readonly bool _ciBuild;

        public Mapping( HotGraph.PackageUpdater packageUpdater, ImmutableArray<BuildSolution> orderedSolutions, bool ciBuild )
        {
            _packageUpdater = packageUpdater;
            _orderedSolutions = orderedSolutions;
            _ciBuild = ciBuild;
        }

        public bool IsEmpty => false;

        public SVersion? GetMappedVersion( string packageId, SVersion from )
        {
            // Packages produced by this World are fully handled here: this lookup handles current target build
            // versions and already built versions: it's useless to lookup the _packageUpdater.GetAlreadyBuiltMapping( bool ciBuild ) mappings.
            if( _packageUpdater.Graph.ProducedPackages.TryGetValue( packageId, out var localSolution ) )
            {
                var b = _orderedSolutions[localSolution.OrderedIndex];
                if( b.MustBuild )
                {
                    return b.BuildInfo.TargetVersion;
                }
                // Ouch... Skippable "+fake" version complicates this!
                // Because we may reach this point with last.TagCommit.Version that is a +fake version. 
                var last = b.VersionInfo.GetLastBuild( _ciBuild );
                return last.VersionMustBuild
                        ? null
                        : last.TagCommit.Version;
            }
            return _packageUpdater.WorldConfiguredMapping.GetMappedVersion( packageId, from )
                    ?? _packageUpdater.DiscrepanciesMapping.GetMappedVersion( packageId, from );
        }

        public PackageMappingType GetMappingType( string packageId )
        {
            // This mirrors the branching of GetMappedVersion above: a package this World produces is answered
            // by the produced branch alone, it never falls back on the bounds nor on the discrepancies.
            if( _packageUpdater.Graph.ProducedPackages.TryGetValue( packageId, out var localSolution ) )
            {
                var b = _orderedSolutions[localSolution.OrderedIndex];
                // A solution that is being built maps every version of its packages to its target version. One
                // that is skipped maps them to its last build, unless that build must itself be rebuilt (the
                // skippable "+fake" case above): there is then no version to offer and nothing to warn about.
                return b.MustBuild || !b.VersionInfo.GetLastBuild( _ciBuild ).VersionMustBuild
                        ? PackageMappingType.Mapped
                        : PackageMappingType.KnownName;
            }
            // Out of this World: GetMappedVersion falls back from the bounds to the discrepancies, so the
            // stronger of the two answers decides. This is the one place the enum's order is used, and it
            // holds only for a "??" chain: "KnownName ?? Mapped" cannot answer null, "KnownName ?? None" can.
            var configured = _packageUpdater.WorldConfiguredMapping.GetMappingType( packageId );
            var discrepancy = _packageUpdater.DiscrepanciesMapping.GetMappingType( packageId );
            return configured > discrepancy ? configured : discrepancy;
        }
    }

}
