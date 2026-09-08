using CK.Core;
using CK.Packaging.Abstractions;
using CKli.ArtifactHandler.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CKli.Publish.Plugin;

/// <summary>
/// Decides whether a <see cref="Roadmap"/> can be published and builds the <see cref="PublishedProfile"/> it leaves
/// on the roadmap's branch. That profile is the published contract itself (<c>CK.Packaging.Abstractions</c>): the
/// <see cref="PublishedFolder"/> stores it as a Json file that anything - a build server, a dashboard - can read.
/// <para>
/// The gate works at the solution level: every package a solution produces carries that solution's single version,
/// and a package identifier is produced by exactly one solution (<see cref="HotGraph.ProducedPackages"/>), so the
/// version a package will carry is a function of its producing solution. Package identifiers are only the
/// join key: the version a consumer was built against is read from its recorded
/// <see cref="BuildContentInfo.Consumed"/> and mapped back to the solution that produces it.
/// </para>
/// <para>
/// The profile is valid when, whatever the subset of its packages a consumer picks, there is no discrepancy among
/// the versions those packages require. A discrepancy in any subset is a discrepancy in the whole set (and any
/// whole-set discrepancy already lives in a two-package subset), so this is decided by a single pass over every
/// recorded requirement.
/// </para>
/// <para>
/// This is the version level counterpart of the branch invariant demonstrated in "HotZone-Workflow.md": that
/// invariant guarantees a package only ever depends on packages produced on its own branch or on a cooler
/// (ancestor) one, and it heals after the fact. Checking the profile before publishing makes it preventive.
/// </para>
/// <para>
/// The gate is per branch and only the branch being published is gated: a publication on a cooler branch always
/// invalidates the profiles of the hotter ones (they still reference the version it supersedes), and those heal
/// through their own next build (<see cref="MustBuildReason.UpstreamVersion"/>). Packages that this World does not
/// produce are out of scope: their alignment belongs to the 'D' discrepancies mapping.
/// </para>
/// </summary>
sealed class PublishedProfileBuilder
{
    readonly ImmutableArray<Discrepancy> _discrepancies;
    readonly ImmutableArray<Roadmap.BuildSolution> _solutionsToBuild;
    readonly ImmutableArray<RepoReleaseInfo> _requiredPublications;
    readonly ImmutableArray<RepoReleaseInfo> _buildingAliens;
    readonly ImmutableArray<RepoReleaseInfo> _missingArtifacts;
    readonly ImmutableArray<RepoReleaseInfo> _alreadyPublishedAliens;

    /// <summary>
    /// A requirement of a solution that disagrees with the version its producer will produce.
    /// </summary>
    /// <param name="Consumer">The solution whose recorded requirement disagrees.</param>
    /// <param name="Producer">The solution that produces the required package.</param>
    /// <param name="Required">The package instance the <paramref name="Consumer"/> was built against.</param>
    /// <param name="Produced">The version the <paramref name="Producer"/> will produce.</param>
    public readonly record struct Discrepancy( Roadmap.BuildSolution Consumer,
                                               Roadmap.BuildSolution Producer,
                                               PackageInstance Required,
                                               SVersion Produced );

    PublishedProfileBuilder( ImmutableArray<Discrepancy> discrepancies,
                             ImmutableArray<Roadmap.BuildSolution> solutionsToBuild,
                             ImmutableArray<RepoReleaseInfo> requiredPublications,
                             ImmutableArray<RepoReleaseInfo> buildingAliens,
                             ImmutableArray<RepoReleaseInfo> missingArtifacts,
                             ImmutableArray<RepoReleaseInfo> alreadyPublishedAliens )
    {
        _discrepancies = discrepancies;
        _solutionsToBuild = solutionsToBuild;
        _requiredPublications = requiredPublications;
        _buildingAliens = buildingAliens;
        _missingArtifacts = missingArtifacts;
        _alreadyPublishedAliens = alreadyPublishedAliens;
    }

    /// <summary>
    /// Gets whether the roadmap can be published: no <see cref="Discrepancies"/> and none of the
    /// <see cref="RequiredPublications"/> is blocked (<see cref="BuildingAliens"/>, <see cref="MissingArtifacts"/>).
    /// </summary>
    public bool IsValid => _discrepancies.Length == 0 && _buildingAliens.Length == 0 && _missingArtifacts.Length == 0;

    /// <summary>
    /// Gets the requirements that disagree with the versions their producers will produce.
    /// </summary>
    public ImmutableArray<Discrepancy> Discrepancies => _discrepancies;

    /// <summary>
    /// Gets the solutions that must be built for the publication to become possible: the <see cref="Discrepancy.Consumer"/>
    /// solutions. Building them rewrites their references from the <see cref="Roadmap.PackageMapping"/>, which aligns them.
    /// </summary>
    public ImmutableArray<Roadmap.BuildSolution> SolutionsToBuild => _solutionsToBuild;

    /// <summary>
    /// Gets the "local/" releases that must be published before the roadmap's own publication: the closure of
    /// producers and consumers of every <see cref="PublishableStatus.IndirectPublishRequired"/> solution.
    /// Ordered producers first: a release can only be published once everything it consumes is on a feed.
    /// </summary>
    public ImmutableArray<RepoReleaseInfo> RequiredPublications => _requiredPublications;

    /// <summary>
    /// Gets the "building/" releases found in the <see cref="RequiredPublications"/> closure. A "building/" version
    /// is the outcome of a failed or incomplete build: there is nothing to publish and this invalidates the profile.
    /// </summary>
    public ImmutableArray<RepoReleaseInfo> BuildingAliens => _buildingAliens;

    /// <summary>
    /// Gets the "local/" releases whose artifacts are no longer available in "$Local": they cannot be published at
    /// all and the only remedy is to rebuild them. This invalidates the profile.
    /// </summary>
    public ImmutableArray<RepoReleaseInfo> MissingArtifacts => _missingArtifacts;

    /// <summary>
    /// Gets the already published releases found in the <see cref="RequiredPublications"/> closure. There should be
    /// none of these: for a producer it means a previous publication was incomplete (some consumers were missed).
    /// This is a warning, not a blocker.
    /// </summary>
    public ImmutableArray<RepoReleaseInfo> AlreadyPublishedAliens => _alreadyPublishedAliens;

    /// <summary>
    /// Computes the gate for the <paramref name="roadmap"/>'s branch.
    /// <para>
    /// This runs before any build: the requirements of the solutions that are not built are the ones recorded in
    /// their version tag (obtained by a real MSBuild evaluation when they were built), and the solutions that are
    /// built consume the roadmap's target versions by construction.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="roadmap">The roadmap to publish.</param>
    /// <param name="versionTag">The version tag plugin (release database for the indirect publication closure).</param>
    /// <returns>The gate or null on error.</returns>
    internal static PublishedProfileBuilder? Create( IActivityMonitor monitor, Roadmap roadmap, VersionTagPlugin versionTag )
    {
        using var g = monitor.OpenTrace( "Computing the publication gate." );

        // Only the solutions that are NOT built can disagree with what will be produced: a built solution has its
        // World references rewritten from the Roadmap.PackageMapping, which is those very versions. The recorded
        // requirements of a solution that is not built are frozen.
        var discrepancies = new List<Discrepancy>();
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( s.MustBuild ) continue;
            var content = s.LastBuild.TagCommit.BuildContentInfo;
            if( content == null ) continue;
            foreach( var c in content.Consumed )
            {
                // Packages not produced by this World are out of scope.
                if( !roadmap.Graph.ProducedPackages.TryGetValue( c.PackageId, out var producerSolution ) ) continue;
                var producer = roadmap.OrderedSolutions[producerSolution.OrderedIndex];
                var produced = producer.TargetVersion;
                if( produced != null && c.Version != produced )
                {
                    monitor.Warn( $"'{s.Repo.DisplayPath}/v{s.LastBuild.Version}' requires '{c}' but '{producer.Repo.DisplayPath}' produces '{produced}'." );
                    discrepancies.Add( new Discrepancy( s, producer, c, produced ) );
                }
            }
        }
        var solutionsToBuild = discrepancies.Select( d => d.Consumer )
                                            .Distinct()
                                            .OrderBy( s => s.Solution.OrderedIndex )
                                            .ToImmutableArray();

        // Everything the profile carries must be on a feed. A "local/" version is built but unpublished: when it
        // belongs to another branch, it and the closure of its producers and consumers must be published first.
        var requiredPublications = ImmutableArray<RepoReleaseInfo>.Empty;
        var buildingAliens = ImmutableArray<RepoReleaseInfo>.Empty;
        var missingArtifacts = ImmutableArray<RepoReleaseInfo>.Empty;
        var alreadyPublishedAliens = ImmutableArray<RepoReleaseInfo>.Empty;
        if( roadmap.PublishableStatus == PublishableStatus.IndirectPublishRequired
            && !CollectRequiredPublications( monitor,
                                             roadmap,
                                             versionTag,
                                             out requiredPublications,
                                             out buildingAliens,
                                             out missingArtifacts,
                                             out alreadyPublishedAliens ) )
        {
            return null;
        }
        return new PublishedProfileBuilder( discrepancies.ToImmutableArray(),
                                            solutionsToBuild,
                                            requiredPublications,
                                            buildingAliens,
                                            missingArtifacts,
                                            alreadyPublishedAliens );
    }

    /// <summary>
    /// Builds the profile that this publication carries on the roadmap's branch, from the real content of the builds
    /// and of the version tags. The set of package identifiers a solution produces is only known once it has been
    /// built, so this is the only place a complete profile exists.
    /// <para>
    /// Building it is also the final check: a conflict here means the publication would carry two versions of one
    /// package identifier. This is what <see cref="Roadmap.PackageMapping"/> cannot express, a single repository
    /// consuming the same package identifier in two versions (conditional package references across target
    /// frameworks) in particular. The same check applies to the profile's
    /// <see cref="PublishedProfile.DirectDependencies"/>: see <see cref="DirectDependencies"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="roadmap">The built roadmap.</param>
    /// <param name="world">The World that publishes: its stack url and name identify the profile.</param>
    /// <param name="profileVersion">
    /// The version of the profile, obtained from <see cref="PublishedFolder.CreateNewProfileVersion"/>: this is the
    /// version of the publication itself, not of any of the packages it carries.
    /// </param>
    /// <returns>The profile or null if it has any conflict (logged).</returns>
    internal PublishedProfile? BuildFinalProfile( IActivityMonitor monitor,
                                                  Roadmap roadmap,
                                                  World world,
                                                  SVersion profileVersion )
    {
        using var g = monitor.OpenTrace( "Building the published profile." );
        var produced = new ProducedPackages();

        // The produced packages: every package identifier each solution produces, in that solution's version.
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( !TryGetFinalContent( s, out var version, out var content ) ) continue;
            foreach( var packageId in content.Produced )
            {
                produced.Add( monitor, s.Repo, packageId, version, $"produced by '{s.Repo.DisplayPath}'" );
            }
        }
        // The requirements: every World package each solution consumes must be carried in the version it consumes.
        // Such a package belongs to the repository that PRODUCES it, not to the one that consumes it.
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( !TryGetFinalContent( s, out _, out var content ) ) continue;
            foreach( var c in content.Consumed )
            {
                if( !roadmap.Graph.ProducedPackages.TryGetValue( c.PackageId, out var producerSolution ) ) continue;
                var producer = roadmap.OrderedSolutions[producerSolution.OrderedIndex];
                produced.Add( monitor, producer.Repo, c.PackageId, c.Version, $"required by '{s.Repo.DisplayPath}'" );
            }
        }
        // The direct dependencies: everything the repositories consume that this profile does not produce.
        // The filter is the produced set built above, NOT roadmap.Graph.ProducedPackages: the graph only sees
        // a project as packable once something in the stack references it, so it under approximates what a
        // solution produces, and a stale reference in a frozen BuildContentInfo.Consumed would then make a
        // produced identifier look external - which the PublishedProfile constructor rejects.
        var direct = new DirectDependencies();
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( !TryGetFinalContent( s, out _, out var content ) ) continue;
            foreach( var c in content.Consumed )
            {
                if( produced.Contains( c.PackageId ) ) continue;
                direct.Add( monitor, s.Repo, c );
            }
        }
        // What a restore brings beyond the direct dependencies: the union of what NuGet resolved
        // transitively for each repository. This is read, never computed - see BuildContentInfo.Transitive.
        var transitive = new TransitiveDependencyUnion();
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( !TryGetFinalContent( s, out _, out var content ) ) continue;
            // A repository that produces nothing has no Repository entry in the profile, so a resolution
            // could not name it: it contributes nothing at all.
            if( !produced.IsProducer( s.Repo ) ) continue;
            if( !content.HasTransitive )
            {
                monitor.Trace( $"'{s.Repo.DisplayPath}' has no recorded transitive packages: they are unknown "
                               + "(which is not the same as knowing there are none), so this repository "
                               + "contributes nothing to the profile's transitive dependencies." );
                continue;
            }
            foreach( var p in content.Transitive )
            {
                transitive.Add( s.Repo.CKliRepoId, p );
            }
        }
        var directDependencies = direct.Build( monitor );
        // A null direct set is already an error: the anchors it provides would be missing.
        var transitiveDependencies = directDependencies == null
                                        ? null
                                        : transitive.Build( monitor, produced, direct );
        return produced.Build( monitor, world, profileVersion, directDependencies, transitiveDependencies );

        static bool TryGetFinalContent( Roadmap.BuildSolution s, out SVersion version, out BuildContentInfo content )
        {
            if( s.MustBuild )
            {
                var result = s.BuildInfo.BuildResult;
                if( result != null )
                {
                    (version, content) = (result.Version, result.Content);
                    return true;
                }
            }
            else if( s.LastBuild.TagCommit.BuildContentInfo != null )
            {
                (version, content) = (s.LastBuild.Version, s.LastBuild.TagCommit.BuildContentInfo);
                return true;
            }
            (version, content) = (null!, null!);
            return false;
        }
    }

    static bool CollectRequiredPublications( IActivityMonitor monitor,
                                             Roadmap roadmap,
                                             VersionTagPlugin versionTag,
                                             out ImmutableArray<RepoReleaseInfo> requiredPublications,
                                             out ImmutableArray<RepoReleaseInfo> buildingAliens,
                                             out ImmutableArray<RepoReleaseInfo> missingArtifacts,
                                             out ImmutableArray<RepoReleaseInfo> alreadyPublishedAliens )
    {
        requiredPublications = buildingAliens = missingArtifacts = alreadyPublishedAliens = ImmutableArray<RepoReleaseInfo>.Empty;

        using var g = monitor.OpenTrace( "Collecting the indirect publications." );
        var releaseDB = versionTag.EnsureDatabase( monitor );
        if( releaseDB == null ) return false;

        // The same release can appear in the closure of several solutions.
        var required = new HashSet<RepoReleaseInfo>();
        var building = new HashSet<RepoReleaseInfo>();
        var published = new HashSet<RepoReleaseInfo>();
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( s.PublishableStatus != PublishableStatus.IndirectPublishRequired ) continue;
            Throw.DebugAssert( s.LastBuild.Version.IsLocal() );
            var info = releaseDB.GetReleaseInfo( monitor,
                                                 s.LastBuild.TagCommit,
                                                 s.LastBuild.Version.CINumber == 0 && s.LastBuild.TagCommit.CI0Version != null );
            var consumers = info.GetAllConsumers( monitor );
            var producers = info.AllProducers;
            Throw.DebugAssert( !consumers.Overlaps( producers ) );
            // Among the consumers and producers one can find "local/" or "building/" versions, but there should not be
            // any published one:
            // - For a consumer it would mean that published packages rely on a non published one.
            // - For a producer it would mean that the producer's publication was not complete: consumers were missed.
            required.Add( info );
            foreach( var c in producers.Concat( consumers ) )
            {
                if( c.Version.IsLocal() ) required.Add( c );
                else if( c.Version.IsBuilding() ) building.Add( c );
                else
                {
                    Throw.DebugAssert( c.Version.ParsedPrefix is null );
                    published.Add( c );
                }
            }
        }
        // A "local/" release whose artifacts are gone from "$Local" cannot be published: only a rebuild can fix it.
        missingArtifacts = required.Where( r => !r.HasAllLocalArtifacts( monitor, out _ ) ).ToImmutableArray();
        requiredPublications = OrderProducersFirst( required );
        buildingAliens = building.ToImmutableArray();
        alreadyPublishedAliens = published.ToImmutableArray();
        return true;

        // Depth first post-order on the producers restricted to the set: a release is emitted only once every
        // release it consumes (and that this closure must publish) has been emitted.
        static ImmutableArray<RepoReleaseInfo> OrderProducersFirst( HashSet<RepoReleaseInfo> releases )
        {
            var result = new List<RepoReleaseInfo>( releases.Count );
            var done = new HashSet<RepoReleaseInfo>();
            foreach( var r in releases ) Visit( r );
            return result.ToImmutableArray();

            void Visit( RepoReleaseInfo r )
            {
                if( !done.Add( r ) ) return;
                foreach( var p in r.DirectProducers )
                {
                    if( releases.Contains( p ) ) Visit( p );
                }
                result.Add( r );
            }
        }
    }

    /// <summary>
    /// Renders the verdict: null when there is nothing to say.
    /// </summary>
    internal IRenderable? ToRenderable( ScreenType screen, Roadmap roadmap )
    {
        IRenderable? r = null;
        if( !_requiredPublications.IsEmpty )
        {
            r = Add( r, screen.Text( $"{_requiredPublications.Length} release(s) from other branches will be published first: '{_requiredPublications.Select( x => x.ToString() ).Concatenate( "', '" )}'.",
                                     TextEffect.Italic ) );
        }
        if( !_alreadyPublishedAliens.IsEmpty )
        {
            r = Add( r, screen.Text( $"⚠ Already published releases found in the closure (a previous publication was incomplete): '{_alreadyPublishedAliens.Select( x => x.ToString() ).Concatenate( "', '" )}'.",
                                     foreColor: ConsoleColor.DarkYellow ) );
        }
        if( IsValid ) return r;

        r = Add( r, screen.Text( $"⚠ Publish blocked: the profile of branch '{roadmap.Graph.BranchName}' would be incoherent.",
                                 foreColor: ConsoleColor.Red ) );
        foreach( var d in _discrepancies )
        {
            r = Add( r, screen.Text( $"  {d.Consumer.Repo.DisplayPath} requires '{d.Required}' but '{d.Producer.Repo.DisplayPath}' produces '{d.Produced}'." ) );
        }
        if( !_solutionsToBuild.IsEmpty )
        {
            r = Add( r, screen.Text( $"Build '{_solutionsToBuild.Select( s => s.Repo.DisplayPath.Path ).Concatenate( "', '" )}' to allow the publication.",
                                     TextEffect.Italic ) );
        }
        if( !_buildingAliens.IsEmpty )
        {
            r = Add( r, screen.Text( $"  Unsuccessful builds in the required publications: '{_buildingAliens.Select( x => x.ToString() ).Concatenate( "', '" )}'.",
                                     foreColor: ConsoleColor.Red ) );
        }
        if( !_missingArtifacts.IsEmpty )
        {
            r = Add( r, screen.Text( $"  Required publications with no local artifacts left (rebuild them): '{_missingArtifacts.Select( x => x.ToString() ).Concatenate( "', '" )}'.",
                                     foreColor: ConsoleColor.Red ) );
        }
        return r;

        static IRenderable Add( IRenderable? r, IRenderable line ) => r == null ? line : r.AddBelow( line );
    }

    /// <summary>
    /// Accumulates the package identifiers a publication carries and the versions its packages require, detecting any
    /// package identifier that would be carried in more than one version, then builds the <see cref="PublishedProfile"/>.
    /// <para>
    /// A package is always attributed to the <see cref="Repo"/> that produces it: this is what turns a flat
    /// "package identifier to version" map into the profile's <see cref="Repository"/> list.
    /// </para>
    /// </summary>
    sealed class ProducedPackages
    {
        readonly Dictionary<string, Entry> _packages;
        int _conflictCount;

        // The Reason only exists for the conflict message: it says where the version comes from.
        readonly record struct Entry( Repo Producer, SVersion Version, string Reason );

        public ProducedPackages()
        {
            _packages = new Dictionary<string, Entry>( StringComparer.OrdinalIgnoreCase );
        }

        /// <summary>
        /// Adds a version for a package identifier. The first one registered is the produced one; a different one is
        /// a conflict: it is logged and <see cref="Build"/> will fail.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="producer">The repository that produces the package.</param>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The version it is produced in.</param>
        /// <param name="reason">Where this version comes from. Appears in the conflict message.</param>
        public void Add( IActivityMonitor monitor, Repo producer, string packageId, SVersion version, string reason )
        {
            if( _packages.TryGetValue( packageId, out var already ) )
            {
                if( already.Version != version )
                {
                    ++_conflictCount;
                    monitor.Error( $"'{packageId}' is produced in '{already.Version}' ({already.Reason}) and in '{version}' ({reason})." );
                }
                return;
            }
            _packages.Add( packageId, new Entry( producer, version, reason ) );
        }

        /// <summary>
        /// Gets whether a package identifier is produced by this publication.
        /// </summary>
        /// <param name="packageId">The package identifier.</param>
        /// <returns>True if the identifier is produced, false otherwise.</returns>
        public bool Contains( string packageId ) => _packages.ContainsKey( packageId );

        /// <summary>
        /// Gets the version a package identifier is produced in.
        /// </summary>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The produced version.</param>
        /// <returns>True if the identifier is produced, false otherwise.</returns>
        public bool TryGetVersion( string packageId, [NotNullWhen( true )] out SVersion? version )
        {
            if( _packages.TryGetValue( packageId, out var e ) )
            {
                version = e.Version;
                return true;
            }
            version = null;
            return false;
        }

        /// <summary>
        /// Gets whether a repository produces at least one package: only such a repository appears in the
        /// profile's <see cref="PublishedProfile.Repositories"/>, and only such a repository can therefore
        /// be named by a <see cref="VersionResolution"/>.
        /// </summary>
        /// <param name="repo">The repository.</param>
        /// <returns>True if the repository produces at least one package, false otherwise.</returns>
        public bool IsProducer( Repo repo ) => _packages.Values.Any( e => e.Producer == repo );

        /// <summary>
        /// Gets the profile or null when any package identifier has been added in more than one version or when
        /// the <paramref name="directDependencies"/> are incoherent.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="world">The publishing World.</param>
        /// <param name="profileVersion">The version of the profile.</param>
        /// <param name="directDependencies">
        /// The profile's direct dependencies or null if they are incoherent (the conflicts have been logged).
        /// </param>
        /// <param name="transitiveDependencies">
        /// What a restore brings beyond them. Null when the <paramref name="directDependencies"/> are.
        /// </param>
        /// <returns>The profile or null.</returns>
        public PublishedProfile? Build( IActivityMonitor monitor,
                                        World world,
                                        SVersion profileVersion,
                                        ImmutableArray<PackageInstance>? directDependencies,
                                        TransitiveDependencies? transitiveDependencies )
        {
            if( _conflictCount > 0 )
            {
                monitor.Error( $"{_conflictCount} package version conflict(s): the publication would carry an incoherent profile." );
            }
            if( _conflictCount > 0 || directDependencies == null )
            {
                return null;
            }
            // Grouping by Repo cannot produce a duplicate url or identifier, and a package identifier appears in a
            // single group since it is a key here: the PublishedProfile constructor's checks cannot fail.
            var repositories = _packages.GroupBy( kv => kv.Value.Producer )
                                        .Select( g => new Repository( new RepositoryKey( g.Key.OriginUrl, g.Key.CKliRepoId ),
                                                                      g.Select( kv => new PackageInstance( kv.Key, kv.Value.Version ) )
                                                                       .ToImmutableArray() ) )
                                        .ToImmutableArray();
            var profile = new PublishedProfile( world.StackRepository.OriginUrl,
                                                world.Name,
                                                profileVersion,
                                                repositories,
                                                directDependencies.Value,
                                                transitiveDependencies );
            monitor.Trace( $"Published profile '{profile}' carries {_packages.Count} produced package(s) from "
                           + $"{repositories.Length} repository(ies), {directDependencies.Value.Length} direct "
                           + $"dependency(ies) and {profile.TransitiveDependencies} transitive one(s)." );
            return profile;
        }
    }

    /// <summary>
    /// Accumulates the packages the publication's repositories consume and that it does not produce - the profile's
    /// <see cref="PublishedProfile.DirectDependencies"/> - detecting any identifier that would be carried in more
    /// than one version.
    /// <para>
    /// The set is expected to be coherent: the 'D' discrepancies mapping aligns every clashing external reference
    /// onto the greatest one (<see cref="Roadmap.PackageMapping"/>) and a solution that disagrees is forced to build
    /// (<see cref="MustBuildReason.DependencyUpdate"/>). So this is an assertion with a message rather than a gate,
    /// and the message names the colliding repositories: that guarantee runs through the shallow read of the project
    /// files while this set comes from the MSBuild evaluated <see cref="BuildContentInfo.Consumed"/>, and the two can
    /// diverge - a repository that this publication did not rebuild keeps a frozen Consumed, a genuine NuGet version
    /// range resolves to something other than the project's text, and conditional package references across target
    /// frameworks legitimately give one identifier two versions inside a single repository.
    /// </para>
    /// <para>
    /// That last case is only a conflict because the model is target framework blind: the day
    /// <see cref="BuildContentInfo.Consumed"/> becomes framework qualified, it stops being one.
    /// </para>
    /// </summary>
    sealed class DirectDependencies
    {
        readonly Dictionary<string, Entry> _packages;
        int _conflictCount;

        // The Consumer only exists for the conflict message: it says which repository requires the version.
        readonly record struct Entry( Repo Consumer, PackageInstance Package );

        public DirectDependencies()
        {
            _packages = new Dictionary<string, Entry>( StringComparer.OrdinalIgnoreCase );
        }

        /// <summary>
        /// Adds a package consumed by a repository. The first version registered is the carried one; a different
        /// one is a conflict: it is logged and <see cref="Build"/> will fail.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="consumer">The repository that consumes the package.</param>
        /// <param name="package">The package instance it consumes.</param>
        public void Add( IActivityMonitor monitor, Repo consumer, PackageInstance package )
        {
            if( _packages.TryGetValue( package.PackageId, out var already ) )
            {
                if( already.Package.Version != package.Version )
                {
                    ++_conflictCount;
                    monitor.Error( $"External package '{package.PackageId}' is consumed by "
                                   + $"'{already.Consumer.DisplayPath}' in '{already.Package.Version}' and by "
                                   + $"'{consumer.DisplayPath}' in '{package.Version}'. The publication cannot "
                                   + "state which version it carries." );
                }
                return;
            }
            _packages.Add( package.PackageId, new Entry( consumer, package ) );
        }

        /// <summary>
        /// Gets the version a package identifier is consumed in.
        /// </summary>
        /// <param name="packageId">The package identifier.</param>
        /// <param name="version">The consumed version.</param>
        /// <returns>True if the identifier is a direct dependency, false otherwise.</returns>
        public bool TryGetVersion( string packageId, [NotNullWhen( true )] out SVersion? version )
        {
            if( _packages.TryGetValue( packageId, out var e ) )
            {
                version = e.Package.Version;
                return true;
            }
            version = null;
            return false;
        }

        /// <summary>
        /// Gets the direct dependencies or null when any package identifier has been consumed in more than one
        /// version. Sorting is the <see cref="PublishedProfile"/>'s business.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The direct dependencies or null.</returns>
        public ImmutableArray<PackageInstance>? Build( IActivityMonitor monitor )
        {
            if( _conflictCount > 0 )
            {
                monitor.Error( $"{_conflictCount} external package version conflict(s): the publication cannot "
                               + "state the versions it is built against." );
                return null;
            }
            return [.. _packages.Values.Select( e => e.Package )];
        }
    }

    /// <summary>
    /// Accumulates what NuGet resolved transitively for each repository of the publication
    /// (<see cref="BuildContentInfo.Transitive"/>) and turns the union into the profile's
    /// <see cref="PublishedProfile.TransitiveDependencies"/>.
    /// <para>
    /// Nothing is walked here and nothing is resolved: each repository's set is already NuGet's own answer -
    /// target framework aware, pruned, one version per identifier per repository. All this does is put the
    /// answers side by side and record where they disagree.
    /// </para>
    /// <para>
    /// Disagreement is expected rather than exceptional: the 'D' mapping aligns the DIRECT external
    /// references across repositories, not their transitive resolutions, so two repositories whose graphs
    /// differ legitimately land on two versions of a package neither of them references. A single repository
    /// can do it alone too - <see cref="BuildResult.ReadConsumedPackages"/> flattens the target frameworks, so
    /// two of them resolving differently gives one identifier two versions.
    /// </para>
    /// </summary>
    sealed class TransitiveDependencyUnion
    {
        // packageId -> resolved version -> the repositories that resolved it.
        readonly Dictionary<string, Dictionary<SVersion, List<RandomId>>> _packages;

        public TransitiveDependencyUnion()
        {
            _packages = new Dictionary<string, Dictionary<SVersion, List<RandomId>>>( StringComparer.OrdinalIgnoreCase );
        }

        /// <summary>
        /// Records that a repository's restore resolved a package instance.
        /// </summary>
        /// <param name="repositoryId">The repository that resolved it.</param>
        /// <param name="package">The resolved package instance.</param>
        public void Add( RandomId repositoryId, PackageInstance package )
        {
            if( !_packages.TryGetValue( package.PackageId, out var versions ) )
            {
                _packages.Add( package.PackageId, versions = new Dictionary<SVersion, List<RandomId>>() );
            }
            if( !versions.TryGetValue( package.Version, out var repositories ) )
            {
                versions.Add( package.Version, repositories = new List<RandomId>() );
            }
            // The same repository appears once per project and per target framework in the recorded set.
            if( !repositories.Contains( repositoryId ) ) repositories.Add( repositoryId );
        }

        /// <summary>
        /// Gets the profile's transitive dependencies. Never null: a publication that recorded nothing
        /// simply has none.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="produced">The produced packages: the first anchor.</param>
        /// <param name="direct">The direct dependencies: the second anchor.</param>
        /// <returns>The transitive dependencies.</returns>
        public TransitiveDependencies Build( IActivityMonitor monitor,
                                             ProducedPackages produced,
                                             DirectDependencies direct )
        {
            var regular = ImmutableArray.CreateBuilder<PackageInstance>();
            var ambiguous = ImmutableArray.CreateBuilder<AmbiguousDependency>();
            foreach( var (packageId, versions) in _packages )
            {
                // An identifier the profile already states is anchored on that statement: only a resolution
                // GREATER than it is worth recording, since a smaller one is invisible to a restore and
                // "an identifier is both stated and transitively resolved" is the common case.
                if( produced.TryGetVersion( packageId, out var anchor ) )
                {
                    AddAnchored( ambiguous, packageId, anchor, VersionSource.ProducedPackages, versions );
                }
                else if( direct.TryGetVersion( packageId, out anchor ) )
                {
                    AddAnchored( ambiguous, packageId, anchor, VersionSource.DirectDependencies, versions );
                }
                else if( versions.Count == 1 )
                {
                    foreach( var (version, _) in versions )
                    {
                        regular.Add( new PackageInstance( packageId, version ) );
                    }
                }
                else
                {
                    // Nothing anchors this identifier: a consumer that takes several of this profile's
                    // packages gets the greatest of the resolutions (NuGet's highest-wins).
                    var resolutions = ToResolutions( versions );
                    var highest = resolutions.Max( r => r.Version )!;
                    ambiguous.Add( new AmbiguousDependency( packageId,
                                                            highest,
                                                            VersionSource.TransitiveDependencies,
                                                            resolutions ) );
                }
            }
            var result = new TransitiveDependencies( regular.DrainToImmutable(), ambiguous.DrainToImmutable() );
            if( !result.Ambiguous.IsEmpty )
            {
                // Out of our control - these are external packages nobody here references - so this is
                // reported, not gated.
                monitor.Warn( $"{result.Ambiguous.Length} transitive package(s) resolved to more than one version "
                              + "across this publication, or to a version this publication does not carry: "
                              + $"{string.Join( ", ", result.Ambiguous.Select( a => a.PackageId ) )}." );
            }
            return result;

            static void AddAnchored( ImmutableArray<AmbiguousDependency>.Builder ambiguous,
                                     string packageId,
                                     SVersion anchor,
                                     VersionSource resolvedFrom,
                                     Dictionary<SVersion, List<RandomId>> versions )
            {
                var greater = versions.Where( kv => kv.Key > anchor ).ToDictionary( kv => kv.Key, kv => kv.Value );
                // Nothing greater: the profile's own entry says it all.
                if( greater.Count == 0 ) return;
                ambiguous.Add( new AmbiguousDependency( packageId, anchor, resolvedFrom, ToResolutions( greater ) ) );
            }

            static ImmutableArray<VersionResolution> ToResolutions( Dictionary<SVersion, List<RandomId>> versions )
            {
                return [.. versions.Select( kv => new VersionResolution( kv.Key, [.. kv.Value] ) )];
            }
        }
    }
}
