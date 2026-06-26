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

    readonly VersionTagPlugin _versionTags;
    readonly BranchModelPlugin _branchModel;
    readonly HotZonePlugin _hotZone;
    readonly RepositoryBuilderPlugin _repoBuilder;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly ShallowSolutionPlugin _solutionPlugin;
    readonly PerfectEventSender<RoadmapBuildEventArgs> _onRoadmapBuild;
    readonly PerfectEventSender<FixBuildEventArgs> _onFixBuild;


    public BuildPlugin( PrimaryPluginContext primaryContext,
                        VersionTagPlugin versionTags,
                        BranchModelPlugin branchModel,
                        HotZonePlugin hotZone,
                        RepositoryBuilderPlugin repoBuilder,
                        ArtifactHandlerPlugin artifactHandler,
                        ShallowSolutionPlugin solutionPlugin )
        : base( primaryContext )
    {
        _versionTags = versionTags;
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
    /// Raised whenever a <see cref="Roadmap"/> has been successfully built.
    /// Note that <see cref="Roadmap.SolutionBuildCount"/> can be 0 (everything was already built and locally available).
    /// </summary>
    public PerfectEvent<RoadmapBuildEventArgs> OnRoadmapBuild => _onRoadmapBuild.PerfectEvent;

    /// <summary>
    /// Raised whenever a fix has been successfully built.
    /// </summary>
    public PerfectEvent<FixBuildEventArgs> OnFixBuild => _onFixBuild.PerfectEvent;

    /// <summary>
    /// Build command.
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
        if( !HandleMaxDoP( monitor, maxDop, out var vMaxDoP )
            || !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest ) )
        {
            return Task.FromResult( false );
        }
        var roadmap = ComputeAndDisplayRoadmap( monitor, context, isPullBuild, ciForce ? CIBuildMode.CIForce : CIBuildMode.CI, mustPublish: publish, branch, all );
        if( roadmap == null )
        {
            return Task.FromResult( false );
        }
        if( dryRun )
        {
            return Task.FromResult( true );
        }
        return DoRunAsync( monitor, context, vMaxDoP, runTest, roadmap );
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
        var roadmap = ComputeAndDisplayRoadmap( monitor, context, isPullBuild, CIBuildMode.None, mustPublish: publish, branch, all );
        if( roadmap == null || !HandleMaxDoP( monitor, maxDop, out var vMaDxDop ) )
        {
            return Task.FromResult( false );
        }
        if( dryRun )
        {
            return Task.FromResult( true );
        }
        bool? runTest = forceTests ? true : null;
        return DoRunAsync( monitor, context, vMaDxDop, runTest, roadmap );
    }

    async Task<bool> DoRunAsync( IActivityMonitor monitor, CKliEnv context, int vMaxDoP, bool? runTest, Roadmap roadmap )
    {
        var results = await roadmap.BuildAsync( monitor, context, this, runTest, vMaxDoP ).ConfigureAwait( false );
        if( results == null )
        {
            return false;
        }
        // Here, results.Length can be 0: everything was already built, we blindly raise the event,
        // it's up to the listeners to handle this (roadmap.SolutionBuildCount and SolutionPublishCount can be 0).
        if( _onRoadmapBuild.HasHandlers )
        {
            var e = new RoadmapBuildEventArgs( monitor, roadmap );
            if( !await _onRoadmapBuild.SafeRaiseAsync( monitor, e ).ConfigureAwait( false ) || !e.Success )
            {
                return false;
            }
        }
        return true;
    }

    Roadmap? ComputeAndDisplayRoadmap( IActivityMonitor monitor,
                                       CKliEnv context,
                                       bool isPullBuild,
                                       CIBuildMode ciBuildMode,
                                       bool mustPublish,
                                       string? branch,
                                       bool all )
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

        var roadmap = Roadmap.Create( monitor, _versionTags, _artifactHandler, hotGraph, isPullBuild, ciBuildMode, mustPublish );
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

    static bool HandleMaxDoP( IActivityMonitor monitor, string? maxDop, out int vMaxDop )
    {
        if( maxDop == null ) vMaxDop = 4;
        else if( !int.TryParse( maxDop, out vMaxDop ) || vMaxDop <= 0 )
        {
            monitor.Error( "Invalid --max-dop value. Must be an integer greater than 0." );
            return false;
        }
        return true;
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
                                             bool forceRebuild = false )
    {
        // Obtain the RepoBuilder for the Repo.
        var repoBuilder = _repoBuilder.Get( monitor, versionInfo.Repo );
        // Should we run the tests?
        runTest ??= !repoBuilder.HasTestRun( monitor, buildCommit );

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
            forceRebuild = true;
        }
        // On success, we will create a new BuildResult with a "local/" version.
        targetVersion = targetVersion.SetParsedPrefix( "local/" );
        var buildInfo = versionInfo.TryGetCommitBuildInfo( monitor, buildCommit, targetVersion, allowRebuild: forceRebuild );
        if( buildInfo == null )
        {
            return null;
        }
        using var gLog = monitor.OpenTrace( $"Core build for '{buildInfo}'." );
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
                                                   runTest.Value ).ConfigureAwait( false );
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
