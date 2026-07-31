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

    [Description( "Builds and publishes the current Fix Workflow." )]
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
        var packageMapper = new PackageMapper();
        var packageMapping = new FixPackageMapper( packageMapper );
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
                                                   packageMapper,
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
                var e = new FixBuildEventArgs( monitor, workflow, results, publish );
                if( !await _onFixBuild.SafeRaiseAsync( monitor, e ).ConfigureAwait( false ) )
                {
                    return false;
                }
            }
        }
        else
        {
            monitor.OpenInfo( $"No listener to the FixBuild event." );
        }
        return true;

        static IRenderable RenderBuildResults( ScreenType s, FixWorkflow workflow, ImmutableArray<BuildResult> results )
        {
            var d = s.Unit.AddBelow( workflow.Targets.Select( t => t.Repo.ToRenderable( s, t.BranchName )
                                                                    .AddRight( s.Text( results[t.Index].Version.ToString() )
                                                                            .Box( marginLeft: 1,
                                                                                  foreColor: results[t.Index].SkippedBuild
                                                                                                ? ConsoleColor.DarkYellow
                                                                                                : ConsoleColor.Green ) ) ) );
            return d.TableLayout();
        }
    }

    async Task<bool> BuildOneFixTargetAsync( IActivityMonitor monitor,
                                             CKliEnv context,
                                             bool? runTest,
                                             bool rebuild,
                                             bool isCIBuild,
                                             ImmutableArray<BuildResult>.Builder bResults,
                                             PackageMapper packageMapper,
                                             FixPackageMapper packageMapping,
                                             FixWorkflow.TargetRepo target )
    {
        var versionInfo = _versionTags.Get( monitor, target.Repo );

        var updated = new PackageMapper();
        if( !CheckoutFixTargetBranch( monitor, target, versionInfo, out var toFix, out int commitDepth )
            || !_solutionPlugin.UpdatePackages( monitor, target.Repo, packageMapping, updated )
            || !CommitUpdatedPackages( monitor, updated, target, out bool hasNewCommit ) )
        {
            return false;
        }
        Throw.DebugAssert( toFix.BuildContentInfo != null );

        if( hasNewCommit )
        {
            commitDepth++;
        }
        var targetVersion = target.TargetVersion;
        if( isCIBuild )
        {
            // The target version already has the incremented Patch number.
            targetVersion = targetVersion.SetCINumber( commitDepth, impactStablePatchNumber: false );
        }
        else
        {
            // We are ready to build or rebuild the target.
            // We have nothing to do when rebuilding: the previous "local/" if it exists, will be
            // moved (the "rolling local build" feature).
            // But when a "fix build --ci" has been done right before, a "--ci" version tag may
            // exist on the same commit we are building. Because this is not allowed, we must
            // handle this case.
            // We decide to be rather aggressive here by cleaning any CI builds (or deprecate them
            // if they have been published... No! this won't work! One cannot rebuild a +deprecated!)
            //
            // TODO!!!
        }

        var result = await CoreBuildAsync( monitor,
                                           context,
                                           versionInfo,
                                           target.Repo.GitRepository.Repository.Head.Tip,
                                           targetVersion.SetParsedPrefix( "local/" ),
                                           runTest,
                                           forceRebuild: rebuild ).ConfigureAwait( false );
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
            packageMapper.Add( p, target.ToFixVersion, targetVersion );
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
