using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

/// <summary>
/// Augments a <see cref="HotGraph"/> and its <see cref="HotGraph.Solution"/> with versions and build actions.
/// <para>
/// This roadmap, just like the graph, cannot compute the number of final packages that will be produced, only the
/// number of solutions. The reason is that we don't require the IsPackable MSBuild flag to be defined, we consider
/// it true if and only if a matching name is "package referenced" by any package in the stack. We require the
/// IsPackable to be set if and only if we find an ambiguity (2 projects with the same name in the stack and that
/// are "package referenced"). With this approach, a new package that is not (yet) referenced across the stack is
/// not "seen" at this level: the real build must be done and <see cref="RepoArtifactInfo.PublishToNuGetLocalFeed(IActivityMonitor, SVersion, string, out ImmutableArray{string})"/>
/// must have been called to know the real produced packages.
/// </para>
/// </summary>
public sealed partial class Roadmap
{
    readonly HotGraph _graph;
    readonly CIBuildMode _ciBuildMode;
    readonly bool _mustPublish;
    readonly bool _dryRun;
    readonly bool _isPullBuild;
    readonly ImmutableArray<BuildSolution> _orderedSolutions;
    readonly ImmutableArray<BuildSolution> _pivots;
    readonly BuildSolutionList _buildSolutions;
    readonly HotGraph.PackageUpdater _packageUpdater;
    readonly Mapping _packageMapping;
    int _buildSolutionCount;
    int _directPublishCount;
    PublishableStatus _publishable;
    bool? _buildSuccess;

    Roadmap( HotGraph graph,
             HotGraph.PackageUpdater packageUpdater,
             bool isPullBuild,
             CIBuildMode ciBuildMode,
             bool mustPublish,
             bool dryRun )
    {
        _graph = graph;
        _packageUpdater = packageUpdater;
        _isPullBuild = isPullBuild;
        _ciBuildMode = ciBuildMode;
        _mustPublish = mustPublish;
        _dryRun = dryRun;
        var buildSolutions = new BuildSolution[graph.Solutions.Count];
        var pivots = graph.HasPivots ? new BuildSolution[graph.Pivots.Count] : buildSolutions;
        int iPivot = 0;
        foreach( var s in graph.OrderedSolutions )
        {
            Throw.DebugAssert( "One solution is a pivot => the graph has pivots.", !s.IsPivot || graph.HasPivots );
            Throw.DebugAssert( "PackageUpdater is available => all SolutionVersionInfo are available.", s.VersionInfo != null );
            var sR = new BuildSolution( this, s, s.VersionInfo );
            buildSolutions[s.OrderedIndex] = sR;
            if( s.IsPivot )
            {
                pivots[iPivot++] = sR;
            }
        }
        _orderedSolutions = ImmutableCollectionsMarshal.AsImmutableArray( buildSolutions );
        _packageMapping = new Mapping( packageUpdater, _orderedSolutions, _ciBuildMode != CIBuildMode.None );
        _pivots = ImmutableCollectionsMarshal.AsImmutableArray( pivots );
        _buildSolutions = new BuildSolutionList( this );
    }

    internal static Roadmap? Create( IActivityMonitor monitor,
                                     VersionTagPlugin versionTag,
                                     ArtifactHandlerPlugin artifactHandler,
                                     HotGraph graph,
                                     bool isPullBuild,
                                     CIBuildMode ciBuildMode,
                                     bool mustPublish,
                                     bool dryRun )
    {

        // Refactor this?...
        // This will introduce a mutable roadmap (just like the graph can be "extended" by new "dev/" solutions).
        // Current implementation is not elegant but may be simpler & safer than its mutable version.
        Roadmap? roadmap;
        bool hasChanged;
        do
        {
            // Computes the PackageUpdater. This is at the level of the HotGraph and handles:
            // - Existing built versions for packages produced by the World.
            // - The VersionTagPlugin World's configuration packages.
            // - And the external package discrepancies across the World.
            //
            // At this stage, this cannot contain the target build versions of the Roadmap: this is why
            // the Roadmap uses a PackageMapping that wraps the HotGraph package mappings to first consider the target versions.
            //
            // (Introducing this layer enables the build process to rely on this PackageMapping as a read only structure that
            // is de facto concurrent-safe: before April 2026, a ConcurrentDictionary was used as a layer above the
            // PackageUpdater.Mappings that was updated by the RoadmapExecutor.DoBuildAsync after each build.)
            //
            var packageUpdater = graph.GetPackageUpdater( monitor, versionTag );
            if( packageUpdater == null ) return null;

            roadmap = new Roadmap( graph, packageUpdater, isPullBuild, ciBuildMode, mustPublish, dryRun );
            if( !roadmap.Initialize( monitor ) )
            {
                return null;
            }
            var buildSolutions = roadmap.OrderedSolutions.Where( s => s.MustBuild ).Select( s => s.Solution );
            if( !graph.ConsiderBuildImpact( monitor, buildSolutions, out hasChanged ) )
            {
                return null;
            }
        }
        while( hasChanged );

        if( !roadmap.ConcludeInitialization( monitor, artifactHandler ) )
        {
            return null;
        }
        return roadmap;
    }

    /// <summary>
    /// Gets the build solutions indexed by <see cref="Repo"/> and ordered by <see cref="Repo.Index"/>.
    /// </summary>
    public BuildSolutionList BuildSolutions => _buildSolutions;

    /// <summary>
    /// Gets the build solutions (ordered by <see cref="HotGraph.Solution.OrderedIndex"/>).
    /// </summary>
    public ImmutableArray<BuildSolution> OrderedSolutions => _orderedSolutions;

    /// <summary>
    /// Gets the pivots solutions if some has been specified.
    /// This is never empty (no pivot means all solutions are pivot: in this case Pivots contains all the solutions).
    /// <para>
    /// This list is ordered by <see cref="Repo.Index"/> but .
    /// </para>
    /// </summary>
    public ImmutableArray<BuildSolution> Pivots => _pivots;

    /// <summary>
    /// Gets the <see cref="HotGraph"/> of the world's solutions.
    /// </summary>
    public HotGraph Graph => _graph;

    /// <summary>
    /// Gets the count of <see cref="OrderedSolutions"/> that have true <see cref="BuildSolution.MustBuild"/>.
    /// </summary>
    public int SolutionBuildCount => _buildSolutionCount;

    /// <summary>
    /// Gets the publishable status: this combines all the <see cref="BuildSolution.PublishableStatus"/>.
    /// This is always computed even when <see cref="MustPublish"/> is false and is at
    /// least <see cref="Plugin.PublishableStatus.AlreadyPublished"/>.
    /// </summary>
    public PublishableStatus PublishableStatus => _publishable;

    /// <summary>
    /// Gets the count of <see cref="OrderedSolutions"/> that must published their build outcome (on the <see cref="HotGraph.BranchName"/>):
    /// their <see cref="BuildSolution.PublishableStatus"/> is either <see cref="PublishableStatus.Build"/>
    /// or <see cref="PublishableStatus.PublishRequired"/> (when already built).
    /// </summary>
    public int DirectPublishCount => _directPublishCount;

    /// <summary>
    /// Gets whether this is a build on the "dev/" branch (produces CI packages).
    /// </summary>
    public bool IsCIBuild => _ciBuildMode != CIBuildMode.None;

    /// <summary>
    /// Gets the package mapping.
    /// <para>
    /// Packages produced by the World are either mapped to already built versions or to build target versions and
    /// dependencies external to the World are mapped by <see cref="HotGraph.PackageUpdater.WorldConfiguredMapping"/>
    /// and by <see cref="HotGraph.PackageUpdater.DiscrepanciesMapping"/>.
    /// </para>
    /// </summary>
    public IPackageMapping PackageMapping => _packageMapping;

    /// <summary>
    /// Gets whether a publication should follow the build.
    /// </summary>
    public bool MustPublish => _mustPublish;

    /// <summary>
    /// Gets whether this is a --dry-run: no real build or publication must be done.
    /// </summary>
    public bool DryRun => _dryRun;

    /// <summary>
    /// Gets whether this roadmap has been built. Always null when <see cref="DryRun"/> is true.
    /// </summary>
    public bool? BuildSuccess => _buildSuccess;

    internal bool Initialize( IActivityMonitor monitor )
    {
        foreach( var s in _orderedSolutions )
        {
            if( !s.Initialize( monitor ) )
            {
                return false;
            }
        }
        Throw.DebugAssert( _orderedSolutions.Count( s => s.MustBuild ) == _buildSolutionCount );
        return true;
    }

    bool ConcludeInitialization( IActivityMonitor monitor, ArtifactHandlerPlugin artifactHandler )
    {
        bool success = true;
        var publishableStatus = PublishableStatus.AlreadyPublished;
        int idxBuildNumber = 1;
        foreach( var s in _orderedSolutions )
        {
            success &= s.ConcludeInitialization( monitor, artifactHandler, ref idxBuildNumber, ref publishableStatus );
            if( s.PublishableStatus is PublishableStatus.Build or PublishableStatus.PublishRequired )
            {
                _directPublishCount++;
            }
        }
        _publishable = publishableStatus;
        return success;
    }

    internal async Task<BuildResult[]?> BuildAsync( IActivityMonitor monitor,
                                                    CKliEnv context,
                                                    BuildPlugin buildPlugin,
                                                    bool? runTest,
                                                    int maxDop,
                                                    CancellationToken cancellation )
    {
        Throw.DebugAssert( _buildSuccess is null && !_dryRun );
        if( _buildSolutionCount == 0 )
        {
            monitor.Info( ScreenType.CKliScreenTag, "No repositories need to be built." );
            _buildSuccess = true;
            return [];
        }
        foreach( var s in _orderedSolutions )
        {
            if( s.Repo.GitStatus.IsDirty )
            {
                int c = _orderedSolutions.Count( s => s.Repo.GitStatus.IsDirty );
                monitor.Error( c > 1
                                ? $"""
                            Git repositories '{_orderedSolutions.Where( s => s.Repo.GitStatus.IsDirty ).Select( s => s.Repo.DisplayPath.Path ).Concatenate( "', '" )}' are dirty.
                            Changes must be committed first.
                            """
                                : $"""
                            Git repository '{_orderedSolutions.First( s => s.Repo.GitStatus.IsDirty ).Repo.DisplayPath.Path}' is dirty.
                            Changes must be committed first.
                            """ );
                _buildSuccess = false;
                return null;
            }
        }
        var builder = new BuildPlugin.RoadmapExecutor( buildPlugin, context, this, runTest, maxDop, cancellation );
        var result = await builder.BuildAsync( monitor );
        if( result != null )
        {
            _buildSuccess = true;
        }
        return result;
    }

    internal struct RStats( int repositoryCount,
                            int buildSolutionCount,
                            bool hasPivots,
                            int pivotsCount,
                            bool isPullBuild,
                            bool isPublish,
                            PublishableStatus publishableStatus,
                            int directPublishCount,
                            IEnumerable<BuildSolution> buildingPending )
    {
        IRenderable? _uDepHead;
        IRenderable? _cDepHead;
        IRenderable? _dDepHead;

        public IRenderable GetUDepHead( ScreenType screen ) => _uDepHead ??= DepKind( screen, "U" );
        public IRenderable GetCDepHead( ScreenType screen ) => _cDepHead ??= DepKind( screen, "C" );
        public IRenderable GetDDepHead( ScreenType screen ) => _dDepHead ??= DepKind( screen, "D" );
        public int UDepUpdates;
        public int CDepUpdates;
        public int DDepUpdates;

        readonly string Action => isPublish ? "publish" : "build";

        public readonly IRenderable Render( ScreenType screen )
        {
            Throw.DebugAssert( (_uDepHead != null) == (UDepUpdates > 0) );
            Throw.DebugAssert( (_cDepHead != null) == (CDepUpdates > 0) );
            Throw.DebugAssert( (_dDepHead != null) == (DDepUpdates > 0) );

            IRenderable r;
            if( buildSolutionCount == 0 )
            {
                var pub = publishableStatus switch
                {
                    PublishableStatus.AlreadyPublished => "and nothing to publish",
                    PublishableStatus.PublishRequired or PublishableStatus.Build => $"but {directPublishCount} can be published",
                    PublishableStatus.IndirectPublishRequired => "(publishing requires publications from other branches)",
                    _ /*PublishableStatus.BuildingPending*/ => "(unable to publish as at least one pending build exist)"
                };

                r = screen.Text( hasPivots
                                 ? pivotsCount > 1
                                   ? $"There is nothing to build from the {pivotsCount} pivots out of {repositoryCount} repositories {pub}."
                                   : $"There is nothing to build from the single pivot out of {repositoryCount} repositories {pub}."
                                 : $"There is nothing to build across the {repositoryCount} repositories {pub}." );

                // Nothing is built: no 'C' nor 'D' update can exist (they always trigger a build) but skipped
                // solutions may have pending 'U' updates.
                Throw.DebugAssert( _cDepHead == null && _dDepHead == null );
                if( _uDepHead != null )
                {
                    r = r.AddBelow( _uDepHead.AddRight( screen.Text( $"{UDepUpdates} update{(UDepUpdates > 1 ? "s" : "")} from upstreams left pending in skipped repositories." ) ) );
                }
                if( !isPullBuild && hasPivots )
                {
                    r = r.AddBelow( screen.Text( $"(Using '*{Action}' may detect required builds in upstreams repositories.)", TextEffect.Italic ) );
                }
            }
            else
            {
                var pub = publishableStatus switch
                {
                    PublishableStatus.AlreadyPublished => "and nothing to publish",
                    PublishableStatus.PublishRequired or PublishableStatus.Build => $"and {directPublishCount} can be published",
                    PublishableStatus.IndirectPublishRequired => "(publishing requires publications from other branches)",
                    _ /*PublishableStatus.BuildingPending*/ => "(unable to publish as at least one pending build exist)"
                };

                r = screen.Text( hasPivots
                                 ? pivotsCount > 1
                                    ? $"Required build for {buildSolutionCount} from the {pivotsCount} pivots out of {repositoryCount} repositories {pub}."
                                    : $"Required build for {buildSolutionCount} from the single pivot out of {repositoryCount} repositories {pub}."
                                 : $"Required build for {buildSolutionCount} repositories across the {repositoryCount} repositories {pub}." );
                if( _uDepHead == null && _cDepHead == null && _dDepHead == null )
                {
                    r = r.AddBelow( screen.Text( $"(No dependency updates other than the ones from the upstreams are needed.)", TextEffect.Italic ) );
                }
                else
                {
                    if( _uDepHead != null )
                    {
                        r = r.AddBelow( _uDepHead.AddRight( screen.Text( $"{UDepUpdates} update{(UDepUpdates > 1 ? "s" : "")} from upstreams." ) ) );
                    }
                    if( _cDepHead != null )
                    {
                        r = r.AddBelow( _cDepHead.AddRight( screen.Text( $"{CDepUpdates} update{(CDepUpdates > 1 ? "s" : "")} from <VersionTag> plugin configuration." ) ) );
                    }
                    if( _dDepHead != null )
                    {
                        r = r.AddBelow( _dDepHead.AddRight( screen.Text( $"{DDepUpdates} update{(DDepUpdates > 1 ? "s" : "")} to fix external dependencies discrepancies." ) ) );
                    }
                }
            }

            if( isPublish && publishableStatus is PublishableStatus.BuildingPending )
            {
                r = r.AddBelow( screen.Text( $"⚠ Publish blocked by unsuccessful build: '{buildingPending.Select( s => $"{s.Repo.DisplayPath}/v{s.LastBuild.Version}" ).Concatenate("', '")}'.", foreColor: ConsoleColor.Red ) );
            }
            return r;
        }

        static IRenderable DepKind( ScreenType screen, string kind )
        {
            return screen.Text( kind, foreColor: ConsoleColor.Black, ConsoleColor.DarkMagenta ).Box( marginRight: 1 );
        }

    }

    internal IRenderable ToRenderable( ScreenType screen )
    {
        int buildIndexLen = _buildSolutionCount switch
        {
            0 => 0,
            < 10 => 1,
            < 100 => 2,
            < 1000 => 3,
            _ => 4
        };

        var stats = new RStats( _orderedSolutions.Length,
                                _buildSolutionCount,
                                _graph.HasPivots,
                                _pivots.Length,
                                _isPullBuild,
                                _mustPublish,
                                _publishable,
                                _directPublishCount,
                                _buildSolutions.Where( s => s.PublishableStatus == PublishableStatus.BuildingPending ) );
        var renderables = ImmutableArray.CreateBuilder<IRenderable>( _orderedSolutions.Length );

        var indexAndRank = new BuildIndexAndRankDisplayState( screen,
                                                              _buildSolutionCount,
                                                              _orderedSolutions.Length,
                                                              i => _orderedSolutions[i].Solution.Rank );

        foreach( BuildSolution s in _orderedSolutions )
        {
            renderables.Add( s.ToRenderable( ref indexAndRank, ref stats ) );
        }
        return new VerticalContent( screen, renderables.MoveToImmutable() ).TableLayout()
               .AddBelow( stats.Render( screen ) );
    }

}
