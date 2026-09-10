using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

/// <summary>
/// The external package upgrades of a World: for each package identifier its repositories consume from the
/// outside, the version they should all be at, and the upgrades that follow for each repository.
/// <para>
/// A target comes from one of three sources, in this order: a <c>&lt;VersionTag&gt;&lt;Packages&gt;</c> pin
/// (which is an authoritative exception and therefore has no target and prunes the closure), the World
/// References' published profiles, and the World's configured NuGet feeds. See <see cref="TryGetTargetAsync"/>.
/// </para>
/// <para>
/// The repositories that participate start with the <see cref="HotGraph.Pivots"/> and grow: an upstream that
/// needs an upgrade joins (it must be rebuilt anyway), and unless <see cref="Options.Narrow"/> is set the
/// downstreams of an updated participant join too - the "wide update" guaranty, which is exactly the set the
/// build that follows would touch.
/// </para>
/// </summary>
public sealed partial class UpgradeMap
{
    readonly HotGraph _graph;
    readonly ImmutableArray<RepoUpgrades> _upgrades;
    readonly ImmutableArray<Target> _targets;
    readonly Options _options;

    /// <summary>
    /// Options that drive the analysis.
    /// </summary>
    /// <param name="Narrow">
    /// True to keep the participants to the pivots and their upstreams. By default the downstreams of an
    /// updated participant join too.
    /// </param>
    /// <param name="ConsiderCI">
    /// True to consider the CI published profiles of the World References. A folder holds at most one alive CI
    /// profile per branch and it is newer than every non CI publication of that branch, so one that is there
    /// applies: there is no "is it superseded" question to answer.
    /// </param>
    /// <param name="StableOnly">
    /// True to consider only <see cref="SVersion.IsStable"/> feed versions, false to consider the prereleases
    /// too. Defaults to whether the analyzed branch is the root one.
    /// </param>
    public sealed record Options( bool Narrow, bool ConsiderCI, bool StableOnly );

    /// <summary>
    /// Where a target comes from, or why there is none.
    /// </summary>
    public enum TargetState
    {
        /// <summary>
        /// No source knows this identifier: no reference anchors it and no feed has it. There is no target.
        /// </summary>
        Unknown,

        /// <summary>
        /// The identifier is pinned by the World's <c>&lt;VersionTag&gt;&lt;Packages&gt;</c> configuration.
        /// There is no target: a pin is an authoritative exception and it prunes the closure.
        /// </summary>
        Pinned,

        /// <summary>
        /// Two World References disagree on the identifier. There is no target: this blocks that package,
        /// not the command.
        /// </summary>
        Conflict,

        /// <summary>
        /// A World Reference anchors the identifier: its version is the target.
        /// </summary>
        Reference,

        /// <summary>
        /// No reference anchors the identifier: the greatest version the World's feeds offer is the target.
        /// </summary>
        Feed
    }

    /// <summary>
    /// The target version of a package identifier and where it comes from.
    /// </summary>
    public sealed class Target
    {
        internal Target( string packageId, SVersion? version, TargetState state, string? origin )
        {
            PackageId = packageId;
            Version = version;
            State = state;
            Origin = origin;
        }

        /// <summary>
        /// Gets the package identifier.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Gets the version every repository should reference. Null when <see cref="HasTarget"/> is false.
        /// </summary>
        public SVersion? Version { get; }

        /// <summary>
        /// Gets where this target comes from, or why there is none.
        /// </summary>
        public TargetState State { get; }

        /// <summary>
        /// Gets a human readable origin: the reference that anchors the version, the feed that offers it, or
        /// the reason there is no target. Null when there is nothing to say.
        /// </summary>
        public string? Origin { get; }

        /// <summary>
        /// Gets whether this identifier has a target version.
        /// </summary>
        public bool HasTarget => Version != null;

        /// <inheritdoc />
        public override string ToString() => HasTarget
                                                ? $"{PackageId} → {Version} ({State})"
                                                : $"{PackageId}: no target ({State})";
    }

    /// <summary>
    /// An upgrade (or a downgrade) of one package of one repository.
    /// </summary>
    /// <param name="Current">The package instance the repository currently references.</param>
    /// <param name="Target">The target version.</param>
    public readonly record struct Upgrade( PackageInstance Current, SVersion Target )
    {
        /// <summary>
        /// Gets whether this moves the version down. A World Reference may legitimately pin lower than what
        /// this World references: alignment is the point, but it is reported apart.
        /// </summary>
        public bool IsDowngrade => Target < Current.Version;

        /// <inheritdoc />
        public override string ToString() => $"{Current.PackageId} {Current.Version} → {Target}";
    }

    /// <summary>
    /// The upgrades of one participating repository.
    /// </summary>
    public sealed class RepoUpgrades
    {
        internal RepoUpgrades( HotGraph.Solution solution, ImmutableArray<Upgrade> upgrades, bool isPivot, bool needsBranch )
        {
            Solution = solution;
            Upgrades = upgrades;
            IsPivot = isPivot;
            NeedsBranch = needsBranch;
        }

        /// <summary>
        /// Gets the solution.
        /// </summary>
        public HotGraph.Solution Solution { get; }

        /// <summary>
        /// Gets the repository.
        /// </summary>
        public Repo Repo => Solution.Repo;

        /// <summary>
        /// Gets the upgrades, ordered by package identifier. Never empty.
        /// </summary>
        public ImmutableArray<Upgrade> Upgrades { get; }

        /// <summary>
        /// Gets whether this repository is one of the <see cref="HotGraph.Pivots"/>.
        /// </summary>
        public bool IsPivot { get; }

        /// <summary>
        /// Gets whether the analyzed branch doesn't exist in this repository: applying the upgrades would
        /// create it (at the commit its <c>BranchLinkType</c> says, which is the content that has been
        /// analyzed here - see <c>HotBranch.GetStartCommit</c>).
        /// </summary>
        public bool NeedsBranch { get; }

        /// <inheritdoc />
        public override string ToString() => $"{Repo.DisplayPath}: {Upgrades.Length} upgrade(s)";
    }

    UpgradeMap( HotGraph graph, Options options, ImmutableArray<RepoUpgrades> upgrades, ImmutableArray<Target> targets )
    {
        _graph = graph;
        _options = options;
        _upgrades = upgrades;
        _targets = targets;
    }

    /// <summary>
    /// Gets the graph this map has been computed from.
    /// </summary>
    public HotGraph Graph => _graph;

    /// <summary>
    /// Gets the options used.
    /// </summary>
    public Options AnalysisOptions => _options;

    /// <summary>
    /// Gets the repositories that must be updated, ordered by <see cref="HotGraph.Solution.OrderedIndex"/>
    /// (upstreams first). Empty when the World is aligned.
    /// </summary>
    public ImmutableArray<RepoUpgrades> Upgrades => _upgrades;

    /// <summary>
    /// Gets every target that has been resolved, ordered by package identifier. This includes the identifiers
    /// that have no target (pinned, conflicting or unknown).
    /// </summary>
    public ImmutableArray<Target> Targets => _targets;

    /// <summary>
    /// Gets whether there is nothing to do.
    /// </summary>
    public bool IsEmpty => _upgrades.Length == 0;

    /// <summary>
    /// Gets the number of upgrades across all the repositories.
    /// </summary>
    public int UpgradeCount => _upgrades.Sum( u => u.Upgrades.Length );

    /// <summary>
    /// Gets the number of downgrades across all the repositories.
    /// </summary>
    public int DowngradeCount => _upgrades.Sum( u => u.Upgrades.Count( x => x.IsDowngrade ) );

    /// <summary>
    /// Computes the map: reads the World References' published profiles, then resolves the target of every
    /// external dependency of the participating repositories, growing the participants until they are stable.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The command context (its secrets store reaches the references and the feeds).</param>
    /// <param name="world">The World.</param>
    /// <param name="graph">The hot graph of the branch to analyze.</param>
    /// <param name="versionTag">The version tag plugin: its <c>&lt;Packages&gt;</c> configuration holds the pins.</param>
    /// <param name="artifactHandler">The artifact handler plugin: its configured feeds are the last source.</param>
    /// <param name="options">The analysis options.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>The map or null on error.</returns>
    public static async Task<UpgradeMap?> CreateAsync( IActivityMonitor monitor,
                                                       CKliEnv context,
                                                       World world,
                                                       HotGraph graph,
                                                       VersionTagPlugin versionTag,
                                                       ArtifactHandlerPlugin artifactHandler,
                                                       Options options,
                                                       CancellationToken cancellation = default )
    {
        Throw.CheckNotNullArgument( graph );
        Throw.CheckNotNullArgument( options );
        using( monitor.OpenInfo( $"Computing the dependency upgrades of branch '{graph.BranchName}'." ) )
        {
            var pins = versionTag.GetPackagesConfiguration( monitor );
            if( pins == null ) return null;
            var references = await ReadReferencesAsync( monitor, context, world, graph.BranchName, options.ConsiderCI, cancellation )
                                        .ConfigureAwait( false );
            if( references == null ) return null;
            if( !artifactHandler.GetConfiguredNuGetFeeds( monitor, out var feeds ) ) return null;

            var resolver = new TargetResolver( monitor, context, pins, references, feeds, options );
            // The upgrades of every solution of the graph: the targets are memoized, so computing them for a
            // solution that ends up out of the participants costs only its own identifiers.
            var perSolution = new List<Upgrade>?[graph.Solutions.Count];
            foreach( var s in graph.Solutions )
            {
                List<Upgrade>? upgrades = null;
                foreach( var p in s.ExternalDependencies )
                {
                    var t = await resolver.TryGetTargetAsync( p.PackageId, cancellation ).ConfigureAwait( false );
                    if( t.Version == null || t.Version == p.Version ) continue;
                    (upgrades ??= new List<Upgrade>()).Add( new Upgrade( p, t.Version ) );
                }
                if( upgrades != null )
                {
                    upgrades.Sort( static ( a, b ) => StringComparer.OrdinalIgnoreCase.Compare( a.Current.PackageId, b.Current.PackageId ) );
                    perSolution[s.Repo.Index] = upgrades;
                }
            }
            var participants = ComputeParticipants( monitor, graph, perSolution, options.Narrow );
            var result = ImmutableArray.CreateBuilder<RepoUpgrades>( participants.Count );
            foreach( var s in graph.OrderedSolutions )
            {
                if( !participants.Contains( s ) ) continue;
                var upgrades = perSolution[s.Repo.Index];
                if( upgrades == null ) continue;
                result.Add( new RepoUpgrades( s,
                                              upgrades.ToImmutableArray(),
                                              s.IsPivot || !graph.HasPivots,
                                              needsBranch: !s.CanBeDevSolution ) );
            }
            return new UpgradeMap( graph, options, result.DrainToImmutable(), resolver.GetTargets() );
        }
    }

    // The participants start with the pivots and grow until stable:
    // - an upstream (AllRequirements is already the transitive closure) that needs an upgrade joins: it must
    //   be rebuilt, and a repository we open a branch on and rebuild is a first class participant;
    // - unless narrow, the downstreams of an updated participant join too. A downstream is exactly a solution
    //   that has the participant in its own AllRequirements, so no reverse edge is needed.
    // Only a solution that has something to update ever joins: one with nothing to do would add no edit.
    static HashSet<HotGraph.Solution> ComputeParticipants( IActivityMonitor monitor,
                                                           HotGraph graph,
                                                           List<Upgrade>?[] perSolution,
                                                           bool narrow )
    {
        var participants = new HashSet<HotGraph.Solution>();
        foreach( var repo in graph.Pivots )
        {
            participants.Add( graph.Solutions[repo.Index] );
        }
        bool changed;
        do
        {
            changed = false;
            // Snapshot: the set is mutated below.
            foreach( var p in participants.ToArray() )
            {
                if( perSolution[p.Repo.Index] == null ) continue;
                foreach( var up in p.AllRequirements )
                {
                    if( perSolution[up.Repo.Index] != null && participants.Add( up ) )
                    {
                        monitor.Trace( $"'{up.Repo.DisplayPath}' joins: it is an upstream of '{p.Repo.DisplayPath}' and must be updated." );
                        changed = true;
                    }
                }
                if( narrow ) continue;
                foreach( var down in graph.Solutions )
                {
                    if( perSolution[down.Repo.Index] != null
                        && down.AllRequirements.Contains( p )
                        && participants.Add( down ) )
                    {
                        monitor.Trace( $"'{down.Repo.DisplayPath}' joins: it is a downstream of the updated '{p.Repo.DisplayPath}'." );
                        changed = true;
                    }
                }
            }
        }
        while( changed );
        return participants;
    }
}
