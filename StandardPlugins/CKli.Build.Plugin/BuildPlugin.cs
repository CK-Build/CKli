using CK.Core;
using CK.PerfectEvent;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

/// <summary>
/// Plugin that implements the build commands.
/// </summary>
[CommandNamespace( "deps",
    """
    Aligns what the World consumes, where "build" propagates what it produces: the external package
    references of its repositories are moved onto the versions its World References publish.
    """,
    Summary = "Aligns the external dependencies of the World.",
    HelpUrl = "https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Build.Plugin/README.md#deps-update-aligning-the-external-dependencies" )]
[CommandNamespace( "fix",
    """
    Builds and publishes a Fix Workflow: "fix build" produces "local/" versions in your own feed,
    "fix publish" the real ones. There is no CI fix build in between.
    """,
    Summary = "Builds and publishes a Fix Workflow.",
    HelpUrl = "https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Build.Plugin/README.md#fix-build--fix-publish-the-fix-workflow" )]
[CommandNamespace( "maintenance",
    "Rebuilds versions that have already been released.",
    HelpUrl = "https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Build.Plugin/README.md#a-note-on-rebuildoldasyncrebuildversionasync" )]
[CommandNamespace( "maintenance rebuild",
    """
    Reproduces a released version on its own commit: the version is rebuilt as it was, never
    incremented. Useful to check that an old release still builds with the current tooling.
    """,
    Summary = "Reproduces a released version on its own commit.",
    HelpUrl = "https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Build.Plugin/README.md#a-note-on-rebuildoldasyncrebuildversionasync" )]
public sealed partial class BuildPlugin : PrimaryPluginBase
{
    const string _dBranch = "Specify the branch to consider. By default, the current head is considered when in a Repo.";
    const string _oBranch = "--branch,-b";
    const string _dMaxDoP = "Maximal Degree of Parallelism of the builds and of the publications. Defaults to 4.";
    const string _dRelease = "Build regular exploratory, prerelease or stable versions instead of CI versions.";
    const string _oRelease = "--release";
    const string _dCIForce = "Build a ci.0 version when a released version is already available on the commit.";
    const string _oCIForce = "--ci.0";
    const string _dSkipTests = "Don't run tests even if they have never locally run on the commit.";
    const string _dForceTests = "Run tests even if they have already run successfully on the commit.";
    const string _dDryRun = "Only display the build roadmap.";
    const string _oDryRun = "--dry-run,-d";

    readonly VersionTagPlugin _versionTag;
    readonly BranchModelPlugin _branchModel;
    readonly HotZonePlugin _hotZone;
    readonly RepositoryBuilderPlugin _repoBuilder;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly ShallowSolutionPlugin _solutionPlugin;
    readonly PerfectEventSender<RoadmapBuildEventArgs> _onRoadmapBuild;
    readonly PerfectEventSender<FixBuildEventArgs> _onFixBuild;
    GitRepository.DistributedLock.Lease? _publishLease;

    static BuilderFunction _builderFunction = RealBuildAsync;

    /// <summary>
    /// Sets the builder function that will be used by this plugin.
    /// <para>
    /// This is mainly for tests, to inject a fake builder (when possible).
    /// </para>
    /// <para>
    /// Note that the "real", default builder function simply checks out the build commit before
    /// calling <see cref="RepoBuilder.BuildAsync(IActivityMonitor, CKliEnv, CommitBuildInfo, bool, CancellationToken)"/>
    /// and restores the repository's working folder to where it was before the build.
    /// </para>
    /// </summary>
    /// <param name="builder">The function or null to use the default implementation.</param>
    /// <returns>The previous builder. Can be used to chain the calls up to the "real", default builder function.</returns>
    public static BuilderFunction SetBuilderFunction( BuilderFunction? builder )
    {
        var previous = _builderFunction;
        _builderFunction = builder ?? RealBuildAsync;
        return previous;
    }

    /// <summary>
    /// Initializes a new BuildPlugin.
    /// </summary>
    /// <param name="primaryContext">The CKli plugin context.</param>
    /// <param name="versionTags">The version tag plugin.</param>
    /// <param name="branchModel">The branch model plugin.</param>
    /// <param name="hotZone">The hot zone plugin.</param>
    /// <param name="repoBuilder">The repo builder plugin.</param>
    /// <param name="artifactHandler">The artifact handler plugin.</param>
    /// <param name="solutionPlugin">The shallow solution plugin.</param>
    public BuildPlugin( PrimaryPluginContext primaryContext,
                        VersionTagPlugin versionTags,
                        BranchModelPlugin branchModel,
                        HotZonePlugin hotZone,
                        RepositoryBuilderPlugin repoBuilder,
                        ArtifactHandlerPlugin artifactHandler,
                        ShallowSolutionPlugin solutionPlugin )
        : base( primaryContext )
    {
        _versionTag = versionTags;
        _branchModel = branchModel;
        _hotZone = hotZone;
        _repoBuilder = repoBuilder;
        _artifactHandler = artifactHandler;
        _solutionPlugin = solutionPlugin;
        World.Events.Issue += IssueRequested;
        _onRoadmapBuild = new PerfectEventSender<RoadmapBuildEventArgs>();
        _onFixBuild = new PerfectEventSender<FixBuildEventArgs>();
    }

    /// <summary>
    /// Raised whenever a <see cref="Roadmap"/> has been successfully built or when <see cref="Roadmap.DryRun"/> is true (no real build must be done).
    /// <para>
    /// Note that <see cref="Roadmap.SolutionBuildCount"/> can be 0 (everything was already built and locally available).
    /// </para>
    /// </summary>
    public PerfectEvent<RoadmapBuildEventArgs> OnRoadmapBuild => _onRoadmapBuild.PerfectEvent;

    /// <summary>
    /// Raised whenever a fix has been successfully built.
    /// </summary>
    public PerfectEvent<FixBuildEventArgs> OnFixBuild => _onFixBuild.PerfectEvent;

    /// <summary>
    /// The name of the World lock that every publishing command takes: this is the very lock that
    /// <c>ckli world lock publish</c> acquires, so a developer can reserve the publication before starting
    /// and <c>ckli world unlock publish</c> is what frees one that a crash left behind.
    /// <para>
    /// This is <see cref="World.PublishLockName"/>: "ckli world lts create" takes it too.
    /// </para>
    /// </summary>
    public const string PublishLockName = World.PublishLockName;

    /// <summary>
    /// The lease duration of <see cref="PublishLockName"/>.
    /// <para>
    /// This is not a budget for the publication - a publication takes as long as its builds take, and the lease
    /// is renewed while they run (see <see cref="GitRepository.DistributedLock.Lease.KeepAlive"/> and
    /// <see cref="GitRepository.DistributedLock.Lease.KeepAliveWhileAsync"/>). It answers the only question a
    /// lease duration can answer: how long a client that crashed - or that was killed between two renewals -
    /// keeps the rest of the team from publishing.
    /// </para>
    /// </summary>
    public static readonly TimeSpan PublishLeaseDuration = TimeSpan.FromMinutes( 15 );

    /// <summary>
    /// Gets the lease of the <see cref="PublishLockName"/> lock while a publishing command runs, null otherwise
    /// (including during a <c>--dry-run</c>, which publishes nothing and must not hold a lock the team shares).
    /// <para>
    /// This is how the roadmap execution and the Publish plugin reach the lease: the lock belongs to the running
    /// command rather than to the roadmap it happens to have computed.
    /// </para>
    /// </summary>
    public GitRepository.DistributedLock.Lease? PublishLease => _publishLease;

    /// <summary>
    /// Runs <paramref name="work"/> while holding the <see cref="PublishLockName"/> lock of the current World.
    /// <para>
    /// The lock covers the WHOLE command, not only the publication step: the roadmap decides which versions are
    /// produced from what the remotes currently carry, so a lock taken after that decision would protect a
    /// decision already made on state somebody else has moved.
    /// </para>
    /// <para>
    /// A lease of this same clone - a reservation taken by <c>ckli world lock publish</c>, or one left behind by
    /// a previous run - is renewed rather than reported as held, and the lock is released when the command ends
    /// whichever way it was obtained: the publication it was reserving is over.
    /// </para>
    /// </summary>
    async Task<bool> UnderPublishLockAsync( IActivityMonitor monitor, bool dryRun, Func<Task<bool>> work )
    {
        // A --dry-run only displays what would happen: it publishes nothing, so making the team wait for it
        // would be a lock protecting nothing.
        if( dryRun ) return await work().ConfigureAwait( false );

        var lockName = $"{World.Name.FullName}-{PublishLockName}";
        if( !World.StackRepository.GetLock( monitor, lockName, out var theLock ) )
        {
            return false;
        }
        var result = theLock.AcquireOrRenew( monitor, PublishLeaseDuration, out var lease, out var holder );
        if( result != GitRepository.DistributedLock.AcquireResult.Acquired )
        {
            if( result == GitRepository.DistributedLock.AcquireResult.Held )
            {
                Throw.DebugAssert( holder != null );
                var remaining = holder.ExpiresAt - DateTimeOffset.UtcNow;
                if( remaining < TimeSpan.Zero ) remaining = TimeSpan.Zero;
                monitor.Error( $"""
                    Unable to publish: '{theLock.LockReference}' is held by
                    {holder.OwnerId}
                    since {holder.AcquiredAt:u}. Unless its holder renews it, it frees itself
                    on {holder.ExpiresAt:u} (in {remaining:hh\:mm\:ss}).
                    Publishing is serialized across the developers of the Stack: wait for it, or ask the holder
                    to run "ckli world unlock {PublishLockName}".
                    """ );
            }
            return false;
        }
        Throw.DebugAssert( lease != null );
        monitor.Info( $"Holding '{theLock.LockReference}' until {lease.ExpiresAt:u} ({PublishLeaseDuration.TotalMinutes:0} minutes, renewed while working)." );
        _publishLease = lease;
        try
        {
            return await work().ConfigureAwait( false );
        }
        finally
        {
            _publishLease = null;
            // Release refuses to delete a reference that is no longer ours, so a lost lease leaves the winner's
            // lock alone. Dispose is the "forgot to release" warning: it stays silent on a lost lease.
            lease.Release( monitor );
            lease.Dispose();
        }
    }

    /// <summary>
    /// Core build command. Upstream repositories are not involved: dependencies are not upgraded.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="branch"></param>
    /// <param name="maxDop"></param>
    /// <param name="release"></param>
    /// <param name="ciForce"></param>
    /// <param name="skipTests"></param>
    /// <param name="forceTests"></param>
    /// <param name="dryRun"></param>
    /// <param name="all"></param>
    /// <returns></returns>
    [Description( "Build-Test-Package and propagates packages from the current repositories to their consumers, keeping them local.",
                  Summary = "Build-Test-Package, keeping the produced packages local." )]
    [CommandPath( "build" )]
    public Task<bool> BuildAsync( IActivityMonitor monitor,
                                  CKliEnv context,
                                  [Description( _dBranch )]
                                  [OptionName( _oBranch )]
                                  string? branch = null,
                                  [Description( _dMaxDoP )]
                                  string? maxDop = null,
                                  [Description( _dRelease )]
                                  [OptionName( _oRelease )]
                                  bool release = false,
                                  [Description( _dCIForce )]
                                  [OptionName(_oCIForce)]
                                  bool ciForce = false,
                                  [Description( _dSkipTests )]
                                  bool skipTests = false,
                                  [Description( _dForceTests )]
                                  bool forceTests = false,
                                  [Description( _dDryRun )]
                                  [OptionName(_oDryRun)]
                                  bool dryRun = false,
                                  [Description( "Build all the Repos, not only the current repositories and their consumers." )]
                                  bool all = false )
    {
        if( !CheckReleaseAndCIForce( monitor, release, ciForce ) ) return Task.FromResult( false );
        return release
            ? DoNonCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, dryRun, isPullBuild: false, publish: false )
            : DoCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, ciForce, dryRun, isPullBuild: false, publish: false );
    }

    /// <summary>
    /// Extends build by publishing the build artefacts on success.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="branch"></param>
    /// <param name="maxDop"></param>
    /// <param name="release"></param>
    /// <param name="ciForce"></param>
    /// <param name="skipTests"></param>
    /// <param name="forceTests"></param>
    /// <param name="dryRun"></param>
    /// <param name="all"></param>
    /// <returns></returns>
    [Description( "Build-Test-Package and propagates packages from the current repositories to their consumers and publishes all the artifacts.",
                  Summary = "Build-Test-Package and publish all the artifacts." )]
    [CommandPath( "publish" )]
    public Task<bool> PublishAsync( IActivityMonitor monitor,
                                    CKliEnv context,
                                    [Description( _dBranch )]
                                    [OptionName( _oBranch )]
                                    string? branch = null,
                                    [Description( _dMaxDoP )]
                                    string? maxDop = null,
                                    [Description( _dRelease )]
                                    [OptionName( _oRelease )]
                                    bool release = false,
                                    [Description( _dCIForce )]
                                    [OptionName(_oCIForce)]
                                    bool ciForce = false,
                                    [Description( _dSkipTests )]
                                    bool skipTests = false,
                                    [Description( _dForceTests )]
                                    bool forceTests = false,
                                    [Description( _dDryRun )]
                                    [OptionName(_oDryRun)]
                                    bool dryRun = false,
                                    [Description( "Publish all the Repos, not only the current repositories and their consumers." )]
                                    bool all = false )
    {
        if( !CheckReleaseAndCIForce( monitor, release, ciForce ) ) return Task.FromResult( false );
        return UnderPublishLockAsync( monitor, dryRun, () => release
          ? DoNonCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, dryRun, isPullBuild: false, publish: true )
          : DoCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, ciForce, dryRun, isPullBuild: false, publish: true ) );
    }

    /// <summary>
    /// "Upstream Build". Upstream repositories are considered. 
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="branch"></param>
    /// <param name="maxDop"></param>
    /// <param name="release"></param>
    /// <param name="ciForce"></param>
    /// <param name="skipTests"></param>
    /// <param name="forceTests"></param>
    /// <param name="dryRun"></param>
    /// <param name="all"></param>
    /// <returns></returns>
    [Description( """
        Upstream closure build: considers the producers of the current repositories, propagates
        packages to their consumers, keeping them local.
        """,
        Summary = "Upstream closure build: builds the producers of the current repositories." )]
    [CommandPath( "*build" )]
    public Task<bool> StarBuildAsync( IActivityMonitor monitor,
                                      CKliEnv context,
                                      [Description( _dBranch )]
                                      [OptionName( _oBranch )]
                                      string? branch = null,
                                      [Description( _dMaxDoP )]
                                      string? maxDop = null,
                                      [Description( _dRelease )]
                                      [OptionName( _oRelease )]
                                      bool release = false,
                                      [Description( _dCIForce )]
                                      [OptionName(_oCIForce)]
                                      bool ciForce = false,
                                      [Description( _dSkipTests )]
                                      bool skipTests = false,
                                      [Description( _dForceTests )]
                                      bool forceTests = false,
                                      [Description( _dDryRun )]
                                      [OptionName(_oDryRun)]
                                      bool dryRun = false,
                                      [Description( "Build all the Repos, not only the ones that consume or produce the current repositories." )]
                                      bool all = false )
    {
        if( !CheckReleaseAndCIForce( monitor, release, ciForce ) ) return Task.FromResult( false );
        return release
         ? DoNonCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, dryRun, isPullBuild: true, publish: false )
         : DoCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, ciForce, dryRun, isPullBuild: true, publish: false );
    }

    /// <summary>
    /// "Upstream Build" and publish artefacts on success.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="branch"></param>
    /// <param name="maxDop"></param>
    /// <param name="release"></param>
    /// <param name="ciForce"></param>
    /// <param name="skipTests"></param>
    /// <param name="forceTests"></param>
    /// <param name="dryRun"></param>
    /// <param name="all"></param>
    /// <returns></returns>
    [Description( """
        Upstream closure publish: considers the producers of the current repositories, propagates
        packages to their consumers and publishes all the artifacts.
        """,
        Summary = "Upstream closure publish: publishes the producers of the current repositories." )]
    [CommandPath( "*publish" )]
    public Task<bool> StarPublishAsync( IActivityMonitor monitor,
                                        CKliEnv context,
                                        [Description( _dBranch )]
                                        [OptionName( _oBranch )]
                                        string? branch = null,
                                        [Description( _dMaxDoP )]
                                        string? maxDop = null,
                                        [Description( _dRelease )]
                                        [OptionName( _oRelease )]
                                        bool release = false,
                                        [Description( _dCIForce )]
                                        [OptionName(_oCIForce)]
                                        bool ciForce = false,
                                        [Description( _dSkipTests )]
                                        bool skipTests = false,
                                        [Description( _dForceTests )]
                                        bool forceTests = false,
                                        [Description( _dDryRun )]
                                        [OptionName(_oDryRun)]
                                        bool dryRun = false,
                                        [Description( "Publish all the Repos, not only the ones that consume or produce the current repositories." )]
                                        bool all = false )
    {
        if( !CheckReleaseAndCIForce( monitor, release, ciForce ) ) return Task.FromResult( false );
        return UnderPublishLockAsync( monitor, dryRun, () => release
         ? DoNonCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, dryRun, isPullBuild: true, publish: true )
         : DoCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, ciForce, dryRun, isPullBuild: true, publish: true ) );
    }

    // "--ci.0" asks for a CI version: it cannot be combined with "--release". Before CI became the default
    // this couldn't be expressed ("--ci.0" simply implied "--ci"), it now has to be refused explicitly.
    static bool CheckReleaseAndCIForce( IActivityMonitor monitor, bool release, bool ciForce )
    {
        if( release && ciForce )
        {
            monitor.Error( $"'{_oRelease}' and '{_oCIForce}' are exclusive: '{_oCIForce}' builds a CI version." );
            return false;
        }
        return true;
    }

    Task<bool> DoCIAsync( IActivityMonitor monitor,
                          CKliEnv context,
                          string? branch,
                          string? maxDop,
                          bool all,
                          bool skipTests,
                          bool forceTests,
                          bool ciForce,
                          bool dryRun,
                          bool isPullBuild,
                          bool publish )
    {
        if( !ParseInteger( monitor, "--max-dop", maxDop, out var vMaxDoP, 4 )
            || !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest ) )
        {
            return Task.FromResult( false );
        }
        var roadmap = ComputeAndDisplayRoadmap( monitor, context, isPullBuild, ciForce ? CIBuildMode.CIForce : CIBuildMode.CI, mustPublish: publish, branch, all, dryRun );
        if( roadmap == null )
        {
            return Task.FromResult( false );
        }
        return dryRun
                ? RaiseRoadmapBuildEvent( monitor, context, roadmap, vMaxDoP )
                : DoRunAsync( monitor, context, vMaxDoP, runTest, roadmap );
    }

    Task<bool> DoNonCIAsync( IActivityMonitor monitor,
                             CKliEnv context,
                             string? branch,
                             string? maxDop,
                             bool all,
                             bool skipTests,
                             bool forceTests,
                             bool dryRun,
                             bool isPullBuild,
                             bool publish )
    {
        if( skipTests )
        {
            monitor.Info( ScreenType.CKliScreenTag, $"The --skip-tests option is ignored when building a release version ('{_oRelease}')." );
        }
        var roadmap = ComputeAndDisplayRoadmap( monitor, context, isPullBuild, CIBuildMode.Release, mustPublish: publish, branch, all, dryRun );
        if( roadmap == null || !ParseInteger( monitor, "--max-dop", maxDop, out var vMaDxDop, 4 ) )
        {
            return Task.FromResult( false );
        }
        return dryRun
                ? RaiseRoadmapBuildEvent( monitor, context, roadmap, vMaDxDop )
                : DoRunAsync( monitor, context, vMaDxDop, runTest: forceTests ? true : null, roadmap );
    }

    async Task<bool> DoRunAsync( IActivityMonitor monitor, CKliEnv context, int vMaxDoP, bool? runTest, Roadmap roadmap )
    {
        var results = await roadmap.BuildAsync( monitor, context, this, runTest, vMaxDoP, PrimaryPluginContext.Cancellation ).ConfigureAwait( false );
        if( results == null )
        {
            return false;
        }
        return await RaiseRoadmapBuildEvent( monitor, context, roadmap, vMaxDoP ).ConfigureAwait( false );
    }

    async Task<bool> RaiseRoadmapBuildEvent( IActivityMonitor monitor, CKliEnv context, Roadmap roadmap, int maxDop )
    {
        // Here, results.Length can be 0: everything was already built, we blindly raise the event,
        // it's up to the listeners to handle this (roadmap.SolutionBuildCount can be 0).
        if( _onRoadmapBuild.HasHandlers )
        {
            using( monitor.OpenTrace( $"Raising RoadmapBuild event." ) )
            {
                var e = new RoadmapBuildEventArgs( monitor, context, World, roadmap, maxDop );
                if( !await _onRoadmapBuild.SafeRaiseAsync( monitor, e ).ConfigureAwait( false ) || !e.Success )
                {
                    return false;
                }
            }
        }
        else
        {
            monitor.Info( $"No listener to the RoadmapBuild event." );
        }
        return true;
    }

    Roadmap? ComputeAndDisplayRoadmap( IActivityMonitor monitor,
                                       CKliEnv context,
                                       bool isPullBuild,
                                       CIBuildMode ciBuildMode,
                                       bool mustPublish,
                                       string? branch,
                                       bool all,
                                       bool dryRun )
    {
        // Consider the repositories selected by current path as the Pivots.
        var pivots = all
                        ? World.GetAllDefinedRepo( monitor )
                        : World.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
        if( pivots == null )
        {
            return null;
        }
        if( branch == null )
        {
            branch = GetBranchName( monitor, pivots[0] );
            for( int  i = 1; i < pivots.Count; ++i )
            {
                var bOther = GetBranchName( monitor, pivots[i] );
                if( bOther != branch )
                {
                    monitor.Error( $"""
                                     Multiple Repo are selected and current checked out branches differ, the --branch <name> must be specified.
                                     (At least, '{pivots[0].DisplayPath}' is on '{branch}' and '{pivots[i].DisplayPath}' is on '{bOther}'.)
                                     """ );
                    return null;
                }
            }
            if( branch == "(no branch)" )
            {
                monitor.Error( $"""
                                A branch must be checked out or the --branch <name> must be specified.
                                (At least, '{pivots[0].DisplayPath}' is on detached head state).
                                """ );
                return null;
            }
            monitor.Info( ScreenType.CKliScreenTag, $"Selecting --branch '{branch}'." );
        }
        // If we are not on a known branch (defined by the Branch Model), give up.
        var branchName = _branchModel.BranchNamespace.FindRequired( monitor, branch );
        if( branchName == null )
        {
            return null;
        }
        // We have a branch name. 

        // When --all is specified, all the repositories are pivots and the actual branch name considered by
        // the hot graph will be the most instable one of all the repositories (but at least as stable as the
        // branchName resolved above of course).
        var hotGraph = _hotZone.GetHotGraph( monitor, branchName, ciBuildMode != CIBuildMode.Release, pivots );
        if( hotGraph == null ) return null;

        var roadmap = Roadmap.Create( monitor, _versionTag, _artifactHandler, hotGraph, isPullBuild, ciBuildMode, mustPublish, dryRun );
        if( roadmap != null  )
        {
            context.Screen.Display( roadmap.ToRenderable );
        }
        return roadmap;

        // The git name of a "dev/" branch is "@lts/dev/X" in a Long Term Support world: RemoveDevPrefix handles it.
        string GetBranchName( IActivityMonitor monitor, Repo r ) => _branchModel.BranchNamespace.RemoveDevPrefix( r.GitStatus.CurrentBranchName, out _ );
    }

    static bool HandleForceSkipTests( IActivityMonitor monitor, bool skipTests, bool forceTests, out bool? runTest )
    {
        runTest = null;
        if( forceTests )
        {
            if( skipTests )
            {
                monitor.Error( $"Invalid flags combination: --skip-test and --force-test cannot be both specified." );
                return false;
            }
            runTest = true;
        }
        else if( skipTests )
        {
            runTest = false;
        }
        return true;
    }

    async Task<BuildResult?> CoreBuildAsync( IActivityMonitor monitor,
                                             CKliEnv context,
                                             VersionTagInfo versionInfo,
                                             Commit buildCommit,
                                             SVersion targetVersion,
                                             bool? runTest,
                                             bool forceRebuild,
                                             CancellationToken cancellation )
    {
        // We reproduce the "building/", "local/" or nothing (published): we don't set
        // the parsed prefix: the version is unchanged.
        Throw.DebugAssert( "When calling this, the targetVersion must specify its final prefix.",
                           targetVersion.ParsedPrefix != null || targetVersion.IsBuildingOrLocal() );

        // Obtain the RepoBuilder for the Repo.
        var repoBuilder = _repoBuilder.Get( monitor, versionInfo.Repo );
        // Should we run the tests?
        runTest ??= !repoBuilder.HasTestRun( monitor, buildCommit );

        if( cancellation.IsCancellationRequested ) return null;

        // If we can avoid the build (because forceRebuild is false), we skip the build only if the tag has not been deleted:
        // this supports a "natural" force rebuild for the user by deleting the version tag.
        if( !forceRebuild )
        {
            if( versionInfo.TryGetTagCommit( targetVersion, out var buildTagCommit ) && !buildTagCommit.IsFakeVersion )
            {
                if( _artifactHandler.HasAllArtifacts( monitor, versionInfo.Repo, targetVersion, buildTagCommit.BuildContentInfo, out var assetsFolder ) )
                {
                    // build is not required... But may be running tests is required.
                    if( !runTest.Value )
                    {
                        // Here, the BuildResult.Version can be "local/" or not but it is synchronized with the tag name.
                        monitor.Info( $"Useless build for '{versionInfo.Repo.DisplayPath}/{targetVersion}' skipped." );
                        return new BuildResult( versionInfo.Repo,
                                                buildTagCommit.Tag,
                                                buildTagCommit.Version,
                                                buildTagCommit.BuildContentInfo,
                                                assetsFolder,
                                                skippedBuild: true );
                    }
                }
            }
        }

        if( cancellation.IsCancellationRequested ) return null;

        // forceRebuild is the only degree of freedom here: it allows the target version to already
        // exist on another commit.
        var buildInfo = versionInfo.TryGetCommitBuildInfo( monitor, buildCommit, targetVersion, allowRebuildVersion: forceRebuild );
        if( buildInfo == null )
        {
            return null;
        }
        // The working folder must not carry what a previous build generated: this is a property of the build
        // itself, not of the BuilderFunction that happens to be installed, so it is done here rather than in
        // RepoBuilder.BuildAsync (which a test harness replaces wholesale).
        if( !_repoBuilder.DeleteBeforeBuild( monitor, versionInfo.Repo ) )
        {
            return null;
        }
        return await _builderFunction( monitor, context, versionInfo, buildCommit, runTest.Value, repoBuilder, buildInfo, cancellation ).ConfigureAwait( false );
    }

    static async Task<BuildResult?> RealBuildAsync( IActivityMonitor monitor,
                                                    CKliEnv context,
                                                    VersionTagInfo versionInfo,
                                                    Commit buildCommit,
                                                    bool runTest,
                                                    RepoBuilder repoBuilder,
                                                    CommitBuildInfo buildInfo,
                                                    CancellationToken cancellation )
    {
        using var gLog = monitor.OpenTrace( $"Core build for '{buildInfo}'." );

        if( cancellation.IsCancellationRequested ) return null;

        //
        // We ensure that the working folder is checked out on the buildCommit content tree.
        // We restore the current branch once we are done.
        // Note that the Branch Head may be a DetachedHead (internal LibGit2Sharp specialization of a Branch) but
        // we don't care: we restore the current state.
        //
        var git = versionInfo.Repo.GitRepository;
        Branch currentHead = git.Repository.Head;
        bool mustCheckOut = currentHead.Tip.Tree.Sha != buildCommit.Tree.Sha;
        if( mustCheckOut )
        {
            monitor.Trace( $"Current working folder content is not the same as the commit '{buildCommit.Sha}' to build. Checking out a detached head." );
            if( !git.Checkout( monitor, buildCommit ) )
            {
                return null;
            }
        }
        else
        {
            if( !git.CheckCleanCommit( monitor ) )
            {
                return null;
            }
        }
        BuildResult? result = null;
        try
        {
            result = await repoBuilder.BuildAsync( monitor,
                                                   context,
                                                   buildInfo,
                                                   runTest,
                                                   cancellation ).ConfigureAwait( false );
        }
        catch( Exception ex )
        {
            monitor.Error( $"Build failed for '{versionInfo.Repo.DisplayPath}' on commit '{buildCommit.Sha}'.", ex );
        }
        if( mustCheckOut )
        {
            monitor.Trace( "Restoring working folder to its previous head." );
            git.Checkout( monitor, currentHead, deleteIgnored: true );
        }
        return result;
    }

}
