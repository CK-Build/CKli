using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

/// <summary>
/// Handles publication of repositories.
/// </summary>
public sealed class PublishPlugin : PrimaryPluginBase
{
    readonly BuildPlugin _build;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly BranchModelPlugin _branchModel;
    readonly VersionTagPlugin _versionTag;

    /// <summary>
    /// Initializes a new publish plugin.
    /// </summary>
    /// <param name="primaryContext">The CKli plugin context.</param>
    /// <param name="build">The build plugin.</param>
    /// <param name="artifactHandler">The artifact handler plugin.</param>
    /// <param name="versionTag">The version tag plugin.</param>
    public PublishPlugin( PrimaryPluginContext primaryContext,
                          BuildPlugin build,
                          ArtifactHandlerPlugin artifactHandler,
                          BranchModelPlugin branchModel,
                          VersionTagPlugin versionTag )
        : base( primaryContext )
    {
        _build = build;
        _artifactHandler = artifactHandler;
        _branchModel = branchModel;
        _versionTag = versionTag;
        _build.OnRoadmapBuild.Async += OnRoadmapBuildAsync;
        _build.OnFixBuild.Async += OnFixBuildAsync;
    }

    async Task OnFixBuildAsync( IActivityMonitor monitor, FixBuildEventArgs e, CancellationToken cancellation )
    {
        if( e.ShouldPublish )
        {
            if( !await PublishAsync( monitor,
                                     World,
                                     _artifactHandler,
                                     _branchModel,
                                     e.FixWorkflow,
                                     e.IsCIBuild,
                                     e.KeepBranchOnSuccessfulPublish,
                                     e.Results,
                                     cancellation ) )
            {
                e.SetFailed();
            }
        }

        static async Task<bool> PublishAsync( IActivityMonitor monitor,
                                              World world,
                                              ArtifactHandlerPlugin artifactHandler,
                                              BranchModelPlugin branchModel,
                                              FixWorkflow fixWorkflow,
                                              bool ciBuild,
                                              bool keepBranchOnSuccessfulPublish,
                                              ImmutableArray<BuildResult> results,
                                              CancellationToken cancel )
        {

            var publisher = DirectPublisher.Create( fixWorkflow, results );

            var packageSender = PackageSender.Create( monitor, artifactHandler, branchModel, world.StackRepository.SecretsStore );
            if( packageSender == null ) return false;

            if( await publisher.PublishAsync( monitor, packageSender, artifactHandler, cancel ).ConfigureAwait( false ) )
            {
                if( !ciBuild )
                {
                    // Instead of complicating the SimplePublisher with this capability that makes sense
                    // only for successful non-CI fix publish, we implement this here as a post-operation:
                    // intermediate publications (halted on error) always keep the already pushed remote
                    // branches. Only the very last successful fix publish applies this default behavior.
                    if( !keepBranchOnSuccessfulPublish )
                    {
                        // We ignore any errors here (they are only logged).
                        foreach( var p in publisher.Repos )
                        {
                            var r = p.Repo.GitRepository;
                            var b = r.Repository.Branches[p.BranchName];
                            if( b != null )
                            {
                                r.DeleteBranch( monitor, b, DeleteGitBranchMode.WithTrackedAndRemoteBranch );
                            }
                        }
                    }
                    FixWorkflow.DeleteCurrent( monitor, world );
                }
                return true;
            }
            return false;
        }
    }


    async Task OnRoadmapBuildAsync( IActivityMonitor monitor, RoadmapBuildEventArgs e, CancellationToken cancellation )
    {
        if( e.ShouldPublish )
        {
            var roadmap = e.Roadmap;
            Throw.DebugAssert( roadmap.PublishableStatus > PublishableStatus.None );
            if( roadmap.PublishableStatus is > PublishableStatus.AlreadyPublished and < PublishableStatus.BuildingPending )
            {
                var publish = PublishRoadmap.Create( e.Monitor, roadmap, _versionTag );
                if( publish != null )
                {
                    e.Screen.Display( publish.ToRenderable );
                    if( !roadmap.DryRun )
                    {
                        var packageSender = PackageSender.Create( monitor, _artifactHandler, _branchModel, e.Context.SecretsStore );
                        if( packageSender == null || !await publish.PublishAsync( e.Monitor, packageSender, _artifactHandler, cancellation ).ConfigureAwait( false ) )
                        {
                            e.SetFailed();
                        }
                    }
                }
            }
        }

    }

}
