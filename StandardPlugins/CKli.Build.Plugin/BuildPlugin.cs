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
public sealed partial class BuildPlugin : PrimaryPluginBase
{
    const string _dBranch = "Specify the branch to consider. By default, the current head is considered when in a Repo.";
    const string _oBranch = "--branch,-b";
    const string _dMaxDoP = "Maximal Degree of Parallelism. Defaults to 4.";
    const string _dCI = "Build CI versions instead of regular exploratory, prerelease or stable versions.";
    const string _oCI = "--ci";
    const string _dCIForce = "Extends --ci to build a ci.0 version when a regular version is available.";
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
    /// Core build command. Upstream repositories are not involved: dependencies are not upgraded.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="branch"></param>
    /// <param name="maxDop"></param>
    /// <param name="ci"></param>
    /// <param name="ciForce"></param>
    /// <param name="skipTests"></param>
    /// <param name="forceTests"></param>
    /// <param name="dryRun"></param>
    /// <param name="all"></param>
    /// <returns></returns>
    [Description( "Build-Test-Package and propagates packages from the current repositories to their consumers, keeping them local." )]
    [CommandPath( "build" )]
    public Task<bool> BuildAsync( IActivityMonitor monitor,
                                  CKliEnv context,
                                  [Description( _dBranch )]
                                  [OptionName( _oBranch )]
                                  string? branch = null,
                                  [Description( _dMaxDoP )]
                                  string? maxDop = null,
                                  [Description( _dCI )]
                                  [OptionName( _oCI )]
                                  bool ci = false,
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
        return ci || ciForce
            ? DoCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, ciForce, dryRun, isPullBuild: false, publish: false )
            : DoNonCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, dryRun, isPullBuild: false, publish: false );
    }

    /// <summary>
    /// Extends build by publishing the build artefacts on success.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="branch"></param>
    /// <param name="maxDop"></param>
    /// <param name="ci"></param>
    /// <param name="ciForce"></param>
    /// <param name="skipTests"></param>
    /// <param name="forceTests"></param>
    /// <param name="dryRun"></param>
    /// <param name="all"></param>
    /// <returns></returns>
    [Description( "Build-Test-Package and propagates packages from the current repositories to their consumers and publishes all the artifacts." )]
    [CommandPath( "publish" )]
    public Task<bool> PublishAsync( IActivityMonitor monitor,
                                    CKliEnv context,
                                    [Description( _dBranch )]
                                    [OptionName( _oBranch )]
                                    string? branch = null,
                                    [Description( _dMaxDoP )]
                                    string? maxDop = null,
                                    [Description( _dCI )]
                                    [OptionName( _oCI )]
                                    bool ci = false,
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
        return ci || ciForce
          ? DoCIAsync( monitor, context, branch, maxDop, all,skipTests, forceTests, ciForce, dryRun, isPullBuild: false, publish: true )
          : DoNonCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, dryRun, isPullBuild: false, publish: true );
    }

    /// <summary>
    /// "Upstream Build". Upstream repositories are considered. 
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="branch"></param>
    /// <param name="maxDop"></param>
    /// <param name="ci"></param>
    /// <param name="ciForce"></param>
    /// <param name="skipTests"></param>
    /// <param name="forceTests"></param>
    /// <param name="dryRun"></param>
    /// <param name="all"></param>
    /// <returns></returns>
    [Description( """Upstream closure build": considers the producers of the current repositories, propagates packages to their consumers, keeping them local.""" )]
    [CommandPath( "*build" )]
    public Task<bool> StarBuildAsync( IActivityMonitor monitor,
                                      CKliEnv context,
                                      [Description( _dBranch )]
                                      [OptionName( _oBranch )]
                                      string? branch = null,
                                      [Description( _dMaxDoP )]
                                      string? maxDop = null,
                                      [Description( _dCI )]
                                      [OptionName( _oCI )]
                                      bool ci = false,
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
        return ci || ciForce
         ? DoCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, ciForce, dryRun, isPullBuild: true, publish: false )
         : DoNonCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, dryRun, isPullBuild: true, publish: false );
    }

    /// <summary>
    /// "Upstream Build" and publish artefacts on success.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="branch"></param>
    /// <param name="maxDop"></param>
    /// <param name="ci"></param>
    /// <param name="ciForce"></param>
    /// <param name="skipTests"></param>
    /// <param name="forceTests"></param>
    /// <param name="dryRun"></param>
    /// <param name="all"></param>
    /// <returns></returns>
    [Description( """Upstream closure publish": considers the producers of the current repositories, propagates packages to their consumers and publishes all the artifacts.""" )]
    [CommandPath( "*publish" )]
    public Task<bool> StarPublishAsync( IActivityMonitor monitor,
                                        CKliEnv context,
                                        [Description( _dBranch )]
                                        [OptionName( _oBranch )]
                                        string? branch = null,
                                        [Description( _dMaxDoP )]
                                        string? maxDop = null,
                                        [Description( _dCI )]
                                        [OptionName( _oCI )]
                                        bool ci = false,
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
        return ci || ciForce
         ? DoCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, ciForce, dryRun, isPullBuild: true, publish: true )
         : DoNonCIAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, dryRun, isPullBuild: true, publish: true );
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
                ? RaiseRoadmapBuildEvent( monitor, context, roadmap )
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
            monitor.Info( ScreenType.CKliScreenTag, "The --skip-tests option is ignored when building a non CI version." );
        }
        var roadmap = ComputeAndDisplayRoadmap( monitor, context, isPullBuild, CIBuildMode.None, mustPublish: publish, branch, all, dryRun );
        if( roadmap == null || !ParseInteger( monitor, "--max-dop", maxDop, out var vMaDxDop, 4 ) )
        {
            return Task.FromResult( false );
        }
        return dryRun
                ? RaiseRoadmapBuildEvent( monitor, context, roadmap )
                : DoRunAsync( monitor, context, vMaDxDop, runTest: forceTests ? true : null, roadmap );
    }

    async Task<bool> DoRunAsync( IActivityMonitor monitor, CKliEnv context, int vMaxDoP, bool? runTest, Roadmap roadmap )
    {
        var results = await roadmap.BuildAsync( monitor, context, this, runTest, vMaxDoP, PrimaryPluginContext.Cancellation ).ConfigureAwait( false );
        if( results == null )
        {
            return false;
        }
        return await RaiseRoadmapBuildEvent( monitor, context, roadmap ).ConfigureAwait( false );
    }

    async Task<bool> RaiseRoadmapBuildEvent( IActivityMonitor monitor, CKliEnv context, Roadmap roadmap )
    {
        // Here, results.Length can be 0: everything was already built, we blindly raise the event,
        // it's up to the listeners to handle this (roadmap.SolutionBuildCount can be 0).
        if( _onRoadmapBuild.HasHandlers )
        {
            using( monitor.OpenTrace( $"Raising RoadmapBuild event." ) )
            {
                var e = new RoadmapBuildEventArgs( monitor, context, World, roadmap );
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
        var hotGraph = _hotZone.GetHotGraph( monitor, branchName, ciBuildMode != CIBuildMode.None, pivots );
        if( hotGraph == null ) return null;

        var roadmap = Roadmap.Create( monitor, _versionTag, _artifactHandler, hotGraph, isPullBuild, ciBuildMode, mustPublish, dryRun );
        if( roadmap != null  )
        {
            context.Screen.Display( roadmap.ToRenderable );
        }
        return roadmap;

        static string GetBranchName( IActivityMonitor monitor, Repo r )
        {
            string branch = r.GitStatus.CurrentBranchName;
            if( branch.StartsWith( "dev/", StringComparison.OrdinalIgnoreCase ) )
            {
                branch = branch.Substring( 4 );
            }
            return branch;
        }
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
