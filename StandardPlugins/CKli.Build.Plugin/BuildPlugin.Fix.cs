using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{
    /// <summary>
    /// Builds the current workflow if its exists.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="ci"></param>
    /// <param name="skipTests"></param>
    /// <param name="forceTests"></param>
    /// <param name="rebuild"></param>
    /// <returns></returns>
    [Description( "Builds the current Fix Workflow." )]
    [CommandPath( "fix build" )]
    public Task<bool> FixBuildAsync( IActivityMonitor monitor,
                                     CKliEnv context,
                                     [Description("Build CI versions instead of the target stable versions.")]
                                     bool ci = false,
                                     [Description( "Don't run tests even if they have never locally run on a commit." )]
                                     bool skipTests = false,
                                     [Description( "Run tests even if they have already run successfully on a commit." )]
                                     bool forceTests = false,
                                     [Description( "Force a rebuild." )]
                                     bool rebuild = false )
    {
        if( !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest )
            || !FixWorkflow.Load( monitor, World, out var workflow ) )
        {
            return Task.FromResult( false );
        }
        return DoBuildFixAsync( monitor, context, runTest, workflow, rebuild, ci, publish: false );
    }


    /// <summary>
    /// Builds and publishes the current workflow if it exists.
    /// The publication is handled by the Publish plugin. By default, fix branches are deleted on success but
    /// may be optionally kept.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="ci"></param>
    /// <param name="rebuild"></param>
    /// <returns></returns>
    [Description( "Builds and publishes the current Fix Workflow. On success, the current workflow is finished." )]
    [CommandPath( "fix publish" )]
    public Task<bool> FixPublishAsync( IActivityMonitor monitor,
                                       CKliEnv context,
                                       [Description( "Publishes CI versions instead of the target stable versions." )]
                                       bool ci = false,
                                       [Description( "Force a rebuild." )]
                                       bool rebuild = false )
    {
        if( !FixWorkflow.Load( monitor, World, out var workflow ) )
        {
            return Task.FromResult( false );
        }
        return DoBuildFixAsync( monitor,
                                context,
                                runTest: rebuild ? true : null,
                                workflow,
                                rebuild,
                                ci,
                                publish: true );
    }

    async Task<bool> DoBuildFixAsync( IActivityMonitor monitor,
                                      CKliEnv context,
                                      bool? runTest,
                                      FixWorkflow? workflow,
                                      bool rebuild,
                                      bool isCIBuild,
                                      bool publish )
    {
        if( workflow == null )
        {
            monitor.Error( $"No current Fix Workflow exist for world '{World.Name}'." );
            return false;
        }
        if( !_hotZone.CheckBasicPreconditions( monitor, $"building '{workflow}'", out var allRepos ) )
        {
            return false;
        }
        var bResults = ImmutableArray.CreateBuilder<BuildResult>( workflow.Targets.Length );
        var packageMapping = new FixPackageMapper();
        foreach( var target in workflow.Targets )
        {
            using( monitor.OpenInfo( $"Building n°{target.Index} - {target.Repo.DisplayPath}" ) )
            {
                if( !await BuildOneFixTargetAsync( monitor,
                                                   context,
                                                   runTest,
                                                   rebuild,
                                                   isCIBuild,
                                                   bResults,
                                                   packageMapping,
                                                   target ).ConfigureAwait( false ) )
                {
                    break;
                }
            }
        }
        if( bResults.Count != workflow.Targets.Length )
        {
            return default;
        }
        var results = bResults.MoveToImmutable();
        var s = context.Screen.ScreenType;
        var display = RenderBuildResults( s, workflow, results );
        context.Screen.Display( display );
        if( _onFixBuild.HasHandlers )
        {
            using( monitor.OpenTrace( $"Raising FixBuild event." ) )
            {
                var e = new FixBuildEventArgs( monitor, workflow, isCIBuild, results, publish );
                if( !await _onFixBuild.SafeRaiseAsync( monitor, e ).ConfigureAwait( false ) )
                {
                    return false;
                }
            }
        }
        else
        {
            monitor.Info( $"No listener to the FixBuild event." );
        }
        return true;

        static IRenderable RenderBuildResults( ScreenType s, FixWorkflow workflow, ImmutableArray<BuildResult> results )
        {
            var d = s.Unit.AddBelow( workflow.Targets.Select( t => t.Repo.ToRenderable( s, t.BranchName )
                                                                    .AddRight( BuildInfo( s, results[t.Index] ) ) ) );
            return d.TableLayout();

            static IRenderable BuildInfo( ScreenType s, BuildResult r )
            {
                return s.Text( r.SkippedBuild ? 'v' + r.Version.ToString() : "→ v" + r.Version.ToString() )
                        .Box( marginLeft: r.SkippedBuild ? 3 : 1,
                              foreColor: r.SkippedBuild ? ConsoleColor.DarkYellow : ConsoleColor.Green );
            }
        }
    }

    async Task<bool> BuildOneFixTargetAsync( IActivityMonitor monitor,
                                             CKliEnv context,
                                             bool? runTest,
                                             bool rebuild,
                                             bool isCIBuild,
                                             ImmutableArray<BuildResult>.Builder bResults,
                                             FixPackageMapper packageMapping,
                                             FixWorkflow.TargetRepo target )
    {
        var versionInfo = _versionTags.Get( monitor, target.Repo );

        // We are ready to build or rebuild the target.
        // We have nothing to do when rebuilding: the previous "local/" if it exists, will be
        // moved (the "rolling local build" feature).
        // But when a "fix build --ci" has been done right before, a "--ci" version tag may
        // exist on the same commit we are building. Because this is not allowed, we must
        // handle this case.
        //  - Aggressive: Cleaning any CI builds (or deprecate the published ones).
        //                Because one cannot rebuild a deprecated (and this is a good feature),
        //                the deprecation must be a "hard delete" (no +deprecated tag)...
        //                That is NOT the spirit so far: published artefacts must be deprecated.
        //                But deprecating a version from here would be weird (surprising remote impact).
        //                
        //  - Gentle: Adding an empty commit when needed (when a CI tag exists).
        //
        //  - Gentle Synthesis: If a "local/" CI build exists, we destroy the release (suppressing the tag
        //                      and any artefacts).
        //                      If a published CI build exists, create an empty commit to carry the release
        //                      and let the user deprecate the version manually whenever he wants.
        //
        //  If the existing version is or has a +fake, this is an error (fake versions have nothing to do in
        //  a fix context).
        //
        var updates = new PackageMapper();
        if( !CheckoutFixTargetBranch( monitor, target, versionInfo, out var toFix, out int commitDepth )
            || !_solutionPlugin.UpdatePackages( monitor, target.Repo, packageMapping, updates )
            || !CommitUpdatedPackages( monitor, updates, target, out bool hasNewCommit ) )
        {
            return false;
        }
        Throw.DebugAssert( toFix.BuildContentInfo != null );

        var commitToBuild = target.Repo.GitRepository.Repository.Head.Tip;
        if( hasNewCommit )
        {
            commitDepth++;
            // A commit has been created by CommitUpdatedPackages.
        }
        else
        {
            // No new commit: we must handle the potential tag clash.
            if( versionInfo.TagCommitsBySha.TryGetValue( commitToBuild.Sha, out var exists ) )
            {
                if( exists.IsOrHasFakeVersion )
                {
                    monitor.Error( $"""
                        Unexpected fake version: {exists.FakeVersion ?? exists} in '{target.Repo.DisplayPath}'.
                        This must be fixed manually.
                        """ );
                    return false;
                }

                if( exists.IsLocal )
                {
                    // The "local/" version exists.
                    // If it's a non-CI build, we must let the build be skipped.
                    // If it's a CI build and we are ci build again, we must let the build be skipped.
                    // => We must only handle a non-CI build on a previous CI build by destroying the
                    //    "local/" build. 
                    if( exists.Version.IsCI && !isCIBuild )
                    {
                        if( !versionInfo.DestroyLocalRelease( monitor, exists.Version, removeFromNuGetGlobalCache: false ) )
                        {
                            return false;
                        }
                    }

                }
                else
                {
                    // The commit has been published.
                    // Only CI builds can be published in a fix workflow via 'ckli fix publish --ci'.
                    // 'ckli fix publish' ends the workflow but if the publication fails, we must let
                    // the build be skipped.
                    // => We only handle a non-CI build on a previously published CI by creating an empty commit.
                    if( exists.Version.IsCI )
                    {
                        var git = target.Repo.GitRepository;
                        if( git.Commit( monitor, $"Skipped published '{exists.Version.ParsedText}'.", CommitBehavior.CreateEmptyCommit ) != CommitResult.Committed )
                        {
                            monitor.Error( $"Unable to create empty commit on branch '{git.CurrentBranchName}' in '{target.Repo.DisplayPath}'." );
                            return false;
                        }
                        commitToBuild = git.Repository.Head.Tip;
                        commitDepth++;
                    }
                }
            }
        }
        var targetVersion = target.TargetVersion;
        if( isCIBuild )
        {
            // The target version already has the incremented Patch number.
            targetVersion = targetVersion.SetCINumber( commitDepth, impactStablePatchNumber: false );
        }
        var result = await CoreBuildAsync( monitor,
                                           context,
                                           versionInfo,
                                           target.Repo.GitRepository.Repository.Head.Tip,
                                           targetVersion.SetParsedPrefix( "local/" ),
                                           runTest,
                                           forceRebuild: rebuild,
                                           PrimaryPluginContext.Cancellation ).ConfigureAwait( false );
        if( result == null )
        {
            return false;
        }
        // We introduce a check here: we demand that the produced package identifiers are the same as the release
        // we are fixing: changing the produced packages that are structural/architectural artifacts is
        // everything but fixing.
        if( !result.SkippedBuild && !result.Content.Produced.SequenceEqual( toFix.BuildContentInfo.Produced ) )
        {
            monitor.Error( $"""
                    Forbidden change in produced packages for a fix in '{target.Repo.DisplayPath}':
                    The version 'v{target.ToFixVersion}' produced packages: '{toFix.BuildContentInfo.Produced.Concatenate( "', '" )}'.
                    But the new fix 'v{targetVersion}' produced: '{result.Content.Produced.Concatenate( "', '" )}'.
                    """ );
            _versionTags.DestroyLocalRelease( monitor, result.Repo, targetVersion );
            return false;
        }
        // Adds the new produced packages to the updates map.
        foreach( var p in result.Content.Produced )
        {
            packageMapping.Add( p, target.ToFixVersion, targetVersion );
        }
        Throw.DebugAssert( bResults.Count == target.Index );
        bResults.Add( result );
        return true;

        static bool CommitUpdatedPackages( IActivityMonitor monitor,
                                           PackageMapper? reusableUpdated,
                                           FixWorkflow.TargetRepo target,
                                           out bool hasNewCommit )
        {
            Throw.DebugAssert( reusableUpdated != null );
            hasNewCommit = false;
            if( !reusableUpdated.IsEmpty )
            {
                var b = new StringBuilder( "Updates: " );
                reusableUpdated.Write( b.AppendLine() );
                var commitResult = target.Repo.GitRepository.Commit( monitor, b.ToString() );
                if( commitResult is CommitResult.Error )
                {
                    return false;
                }
                hasNewCommit = commitResult is not CommitResult.NoChanges;
                reusableUpdated.Clear();
            }
            return true;
        }

        static bool CheckoutFixTargetBranch( IActivityMonitor monitor,
                                             FixWorkflow.TargetRepo target,
                                             VersionTagInfo versionInfo,
                                             [NotNullWhen( true )] out TagCommit? toFix,
                                             out int commitDepth )
        {
            commitDepth = 0;
            // We must be able to retrieve the TagCommit to fix.
            if( !versionInfo.TryGetTagCommit( target.ToFixVersion, out toFix ) )
            {
                monitor.Error( $"Unable to find the commit '{target.ToFixCommitSha}' version 'v{target.ToFixVersion}' to be fixed in '{target.Repo.DisplayPath}'." );
                return false;
            }
            if( toFix.BuildContentInfo == null )
            {
                monitor.Error( $"The version 'v{target.ToFixVersion}' of the commit '{target.ToFixCommitSha}' to be fixed in '{target.Repo.DisplayPath}' has no more a valid build content." );
                return false;
            }

            GitRepository gitRepository = target.Repo.GitRepository;

            var branch = gitRepository.GetBranch( monitor, target.BranchName, LogLevel.Error );
            if( branch == null )
            {
                return false;
            }
            commitDepth = gitRepository.ComputeCommitDepth( monitor, toFix.Commit, branch.Tip );
            if( commitDepth < 0 )
            {
                monitor.Error( $"""
                    Unable to compute commit depth.
                    Branch '{target.BranchName}' in '{target.Repo.DisplayPath}' may not be related to the commit '{target.ToFixCommitSha}' version 'v{target.ToFixVersion}' to be fixed.
                    """ );
                return false;
            }
            return gitRepository.Checkout( monitor, branch );
        }

    }

}
