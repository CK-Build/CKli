using CK.Core;
using CKli.ShallowSolution.Plugin;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CKli.HotZone.Plugin;


public sealed partial class HotGraph
{
    /// <summary>
    /// Exposes different <see cref="IPackageMapping"/> that must be used to update package dependencies in the <see cref="Graph"/>.
    /// <para>
    /// This is obtained by <see cref="HotGraph.GetPackageUpdater(IActivityMonitor, VersionTag.Plugin.VersionTagPlugin)"/>.
    /// Requires that <see cref="CKli.VersionTag.Plugin.VersionTagPlugin"/> has no issue.
    /// </para>
    /// </summary>
    public sealed class PackageUpdater
    {
        readonly HotGraph _graph;
        readonly ImmutableArray<SolutionVersionInfo> _versions;
        IReadOnlyDictionary<string, IReadOnlyList<(Solution Solution, SVersion Version)>>? _discrepancies;
        IPackageMapping? _alreadyBuiltMappingInCI;
        IPackageMapping? _alreadyBuiltMappingInNonCI;
        IPackageMapping? _discrepanciesMapping;

        internal PackageUpdater( HotGraph graph, ImmutableArray<SolutionVersionInfo> versions )
        {
            _graph = graph;
            _versions = versions;
        }

        /// <summary>
        /// Gets the hot graph.
        /// </summary>
        public HotGraph Graph => _graph;

        /// <summary>
        /// Maps package name to one or more Solution in which their <see cref="Solution.ExternalDependencies"/> have different versions.
        /// <para>
        /// A solution may appear more than once if more than one of its project references different versions.
        /// </para>
        /// <para>
        /// This is computed on demand and cached and is the base for the <see cref="DiscrepanciesMapping"/>. 
        /// </para>
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<(Solution Solution, SVersion Version)>> Discrepancies
        {
            get
            {
                if( _discrepancies == null )
                {
                    var firstMet = new Dictionary<string, (Solution Solution, SVersion Version)>();
                    var result = new Dictionary<string, IReadOnlyList<(Solution Solution, SVersion Version)>>();
                    foreach( var s in _versions )
                    {
                        foreach( var p in s.Solution.ExternalDependencies )
                        {
                            if( firstMet.TryGetValue( p.PackageId, out var exists ) )
                            {
                                if( exists.Version != p.Version )
                                {
                                    AddDiscrepancy( result, p.PackageId, exists, (s.Solution, p.Version) );
                                }
                            }
                            else
                            {
                                firstMet.Add( p.PackageId, (s.Solution, p.Version) );
                            }
                        }
                    }
                    _discrepancies = result;

                    static void AddDiscrepancy( Dictionary<string, IReadOnlyList<(Solution Solution, SVersion Version)>> result,
                                                string packageId,
                                                in (Solution Solution, SVersion Version) exists,
                                                in (Solution Solution, SVersion Version) clash )
                    {
                        if( result.TryGetValue( packageId, out var clashes ) )
                        {
                            Unsafe.As<List<(Solution Solution, SVersion Version)>>( clashes ).Add( clash );
                        }
                        else
                        {
                            result.Add( packageId, new List<(Solution Solution, SVersion Version)> { exists, clash } );
                        }
                    }
                }
                return _discrepancies;
            }

        }

        /// <summary>
        /// Gets the package mapping with the current <see cref="SolutionVersionInfo.GetLastBuild(bool)"/> version of each solution
        /// excluding true <see cref="SolutionVersionInfo.LastBuiltVersion.VersionMustBuild"/>: when the version is a "+fake" or a "+deprecated",
        /// mapping is ignored.
        /// </summary>
        public IPackageMapping GetAlreadyBuiltMapping( bool ciBuild )
        {
            return ciBuild
                    ? _alreadyBuiltMappingInCI ??= new LastBuildVersionMapping( _graph._p2s, _versions, true )
                    : _alreadyBuiltMappingInNonCI ??= new LastBuildVersionMapping( _graph._p2s, _versions, false );
        }

        /// <summary>
        /// Gets the version mapping of the configured Worlds packages from the <see cref="VersionTag.Plugin.VersionTagPlugin.GetPackagesConfiguration(IActivityMonitor)"/>.
        /// <para>
        /// The configuration carries a <see cref="SVersionBound"/> per package: a referenced version that is in
        /// its bound is left alone, one that is not is mapped to the bound's <see cref="SVersionBound.Base"/>.
        /// </para>
        /// </summary>
        public IPackageMapping WorldConfiguredMapping => _graph._externalPackages;

        /// <summary>
        /// Gets the version mapping that resolves <see cref="Discrepancies"/> by mapping to the greatest referenced version.
        /// </summary>
        public IPackageMapping DiscrepanciesMapping => _discrepanciesMapping ??= BrutalPackageMapper.Create( Discrepancies.ToDictionary( kv => kv.Key,
                                                                                                                                         kv => kv.Value.Max( sv => sv.Version )!,
                                                                                                                                         StringComparer.OrdinalIgnoreCase ) );

        sealed class LastBuildVersionMapping : IPackageMapping
        {
            readonly Dictionary<string, Solution> _p2s;
            readonly ImmutableArray<SolutionVersionInfo> _versions;
            readonly bool _ciBuild;

            public LastBuildVersionMapping( Dictionary<string, Solution> p2s, ImmutableArray<SolutionVersionInfo> versions, bool ciBuild )
            {
                _p2s = p2s;
                _versions = versions;
                _ciBuild = ciBuild;
            }

            public bool IsEmpty => _p2s.Count == 0;

            public SVersion? GetMappedVersion( string packageId, SVersion from )
            {
                if( _p2s.TryGetValue( packageId, out var s ) )
                {
                    var sv = _versions[s.Repo.Index];
                    var v = sv.GetLastBuild( _ciBuild );
                    if( v.VersionMustBuild )
                    {
                        return null;
                    }
                    return _ciBuild && v.TagCommit.CI0Version != null
                            ? v.TagCommit.CI0Version
                            : v.TagCommit.Version;
                }
                return null;
            }

            // A solution whose last build must itself be rebuilt (a "+fake" or a deprecated version) has no
            // version to offer: the identifier is known but nothing maps, and that is not an anomaly.
            public PackageMappingType GetMappingType( string packageId )
            {
                if( !_p2s.TryGetValue( packageId, out var s ) ) return PackageMappingType.None;
                return _versions[s.Repo.Index].GetLastBuild( _ciBuild ).VersionMustBuild
                        ? PackageMappingType.KnownName
                        : PackageMappingType.Mapped;
            }
        }

    }
}
