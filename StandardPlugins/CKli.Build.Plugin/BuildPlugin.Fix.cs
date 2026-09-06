using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;

using System;
using System.Collections.Generic;
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
    /// <param name="skipTests"></param>
    /// <param name="forceTests"></param>
    /// <param name="rebuild"></param>
    /// <returns></returns>
    [Description( "Builds the current Fix Workflow. The produced versions are local ones: use 'ckli fix push' to share them." )]
    [CommandPath( "fix build" )]
    public Task<bool> FixBuildAsync( IActivityMonitor monitor,
                                     CKliEnv context,
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
        return DoBuildFixAsync( monitor, context, runTest, workflow, rebuild, publish: false, keepBranch: true );
    }


    /// <summary>
    /// Builds and publishes the current workflow if it exists.
    /// The publication is handled by the Publish plugin. By default, fix branches are deleted on success but
    /// may be optionally kept.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="keepBranch"></param>
    /// <param name="rebuild"></param>
    /// <returns></returns>
    [Description( "Builds and publishes the current Fix Workflow. On success, the current workflow is finished." )]
    [CommandPath( "fix publish" )]
    public Task<bool> FixPublishAsync( IActivityMonitor monitor,
                                       CKliEnv context,
                                       [Description( "On success, keeps the 'fix/' branches instead of deleting them." )]
                                       bool keepBranch = false,
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
                                publish: true,
                                keepBranch );
    }

    async Task<bool> DoBuildFixAsync( IActivityMonitor monitor,
                                      CKliEnv context,
                                      bool? runTest,
                                      FixWorkflow? workflow,
                                      bool rebuild,
                                      bool publish,
                                      bool keepBranch )
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
                                                   bResults,
                                                   packageMapping,
                                                   target ).ConfigureAwait( false ) )
                {
                    break;
                }
            }
        }
        // If all of them have been produced: success!
        // Commit any "building/" prefix to the "local/" one.
        if( bResults.Count != workflow.Targets.Length
            || !CommitBuildingTags( monitor, bResults ) )
        {
            return false;
        }

        var results = bResults.MoveToImmutable();
        var s = context.Screen.ScreenType;
        var display = RenderBuildResults( s, workflow, results );
        context.Screen.Display( display );
        if( _onFixBuild.HasHandlers )
        {
            using( monitor.OpenTrace( $"Raising FixBuild event." ) )
            {
                var e = new FixBuildEventArgs( monitor, context, workflow, results, publish, keepBranch );
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

        static bool CommitBuildingTags( IActivityMonitor monitor, IEnumerable<BuildResult> results )
        {
            var buildings = results.Where( r => r.Version.IsBuilding() ).ToList();
            if( buildings.Count > 0 )
            {
                using( monitor.OpenInfo( $"Build succeed: committing {buildings.Count} 'building/ versions to 'local/' ones." ) )
                {
                    try
                    {
                        foreach( var r in buildings )
                        {
                            r.CommitBuilding();
                        }
                    }
                    catch( Exception ex )
                    {
                        monitor.Error( "While committing 'building/ versions to 'local/' ones.", ex );
                        return false;
                    }
                }
            }
            return true;
        }

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
                                             ImmutableArray<BuildResult>.Builder bResults,
                                             FixPackageMapper packageMapping,
                                             FixWorkflow.TargetRepo target )
    {
        var versionInfo = _versionTag.Get( monitor, target.Repo );

        // We are ready to build or rebuild the target.
        // We have nothing to do when rebuilding: the previous "building/" or "local/" if it exists, will be
        // moved (the "rolling local build" feature).
        //
        // A CI version tag may nevertheless be found on the commit we are building. The fix workflow cannot
        // produce one any more (there is no "fix build --ci"), so it can only be a leftover from before that
        // mode was removed. A commit bears at most one version, so it must be handled rather than tripped
        // over:
        //  - A "building/" or "local/" CI build is unpublished: we destroy the release (its tag and artefacts).
        //  - A published CI build cannot be touched (published artefacts are deprecated, never deleted, and
        //    deprecating from here would be a surprising remote effect): we add an empty commit to carry the
        //    new release and leave the old version alone.
        //
        //  If the existing version is or has a +fake, this is an error (fake versions have nothing to do in
        //  a fix context).
        //
        var updates = new PackageMapper();
        if( !CheckoutFixTargetBranch( monitor, target, versionInfo, out var toFix )
            || !_solutionPlugin.UpdatePackages( monitor, target.Repo, packageMapping, updates )
            || !CommitUpdatedPackages( monitor, updates, target, out bool hasNewCommit ) )
        {
            return false;
        }
        Throw.DebugAssert( toFix.BuildContentInfo != null );

        var commitToBuild = target.Repo.GitRepository.Repository.Head.Tip;
        if( !hasNewCommit )
        {
            // No commit has been created by CommitUpdatedPackages.
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

                if( exists.IsBuildingOrLocal )
                {
                    // The version is unpublished. A non-CI one is this workflow's own previous build: we let
                    // it be skipped or moved. A CI one is a leftover: destroy it, a commit bears one version.
                    if( exists.Version.IsCI
                        && !versionInfo.DestroyLocalRelease( monitor, exists.Version, removeFromNuGetGlobalCache: false ) )
                    {
                        return false;
                    }
                }
                else
                {
                    // The commit has been published. A non-CI one is a 'ckli fix publish' whose publication
                    // failed after the tag was applied: we let the build be skipped. A published CI one is a
                    // leftover that must not be touched: carry the new release on an empty commit instead.
                    if( exists.Version.IsCI )
                    {
                        var git = target.Repo.GitRepository;
                        if( git.Commit( monitor, $"Skipped published '{exists.Version.ParsedText}'.", CommitBehavior.CreateEmptyCommit ) != CommitResult.Committed )
                        {
                            monitor.Error( $"Unable to create empty commit on branch '{git.CurrentBranchName}' in '{target.Repo.DisplayPath}'." );
                            return false;
                        }
                        commitToBuild = git.Repository.Head.Tip;
                    }
                }
            }
        }
        var targetVersion = target.TargetVersion.SetParsedPrefix( "building/" );
        // A previous "fix build" of this workflow may already have produced the target version. When it sits
        // on another commit, this build must MOVE it onto the new one (the "rolling local build"), and
        // CoreBuildAsync only allows that when forceRebuild is set - the very same flag that, when unset,
        // lets a useless build be skipped. The two cases must therefore be told apart here: the roadmap can
        // answer with a plain "!TargetVersion.IsCI" because it only calls CoreBuildAsync for the solutions it
        // already decided to build, whereas every fix target goes through it.
        bool moveVersion = versionInfo.TryGetTagCommit( target.TargetVersion, out var alreadyBuilt )
                           && alreadyBuilt.Commit.Sha != commitToBuild.Sha;
        var result = await CoreBuildAsync( monitor,
                                           context,
                                           versionInfo,
                                           commitToBuild,
                                           targetVersion,
                                           runTest,
                                           forceRebuild: rebuild || moveVersion,
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
            _versionTag.DestroyLocalRelease( monitor, result.Repo, targetVersion );
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
                                             [NotNullWhen( true )] out TagCommit? toFix )
        {
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
            // The depth itself is no longer used (it fed the CI number of the removed "fix build --ci"),
            // but a negative answer is how an unrelated branch is detected.
            if( gitRepository.ComputeCommitDepth( monitor, toFix.Commit, branch.Tip ) < 0 )
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
