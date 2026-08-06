using CK.Core;
using CKli.ArtifactHandler.Plugin;
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
    readonly VersionTagPlugin _versionTag;

    public PublishPlugin( PrimaryPluginContext primaryContext,
                          BuildPlugin build,
                          ArtifactHandlerPlugin artifactHandler,
                          VersionTagPlugin versionTag )
        : base( primaryContext )
    {
        _build = build;
        _artifactHandler = artifactHandler;
        _versionTag = versionTag;
        _build.OnRoadmapBuild.Async += OnRoadmapBuildAsync;
        _build.OnFixBuild.Async += OnFixBuildAsync;
    }

    async Task OnFixBuildAsync( IActivityMonitor monitor, FixBuildEventArgs e, CancellationToken cancel )
    {
        if( e.ShouldPublish )
        {
            if( !await PublishAsync( monitor, World, _artifactHandler, _versionTag, e.BuildDate, e.FixWorkflow, e.Results, cancel ) )
            {
                e.SetFailed();
            }
        }

        static async Task<bool> PublishAsync( IActivityMonitor monitor,
                                              World world,
                                              ArtifactHandlerPlugin artifactHandler,
                                              VersionTagPlugin versionTag,
                                              DateTime buildDate,
                                              FixWorkflow fixWorkflow,
                                              ImmutableArray<BuildResult> results,
                                              CancellationToken cancel )
        {
            // A fix is on the stable branch.
            var packageSender = PackageSender.Create( monitor, prereleaseName: "", ciBuild: false, artifactHandler, world.StackRepository.SecretsStore );
            if( packageSender == null ) return false;

            var state = new PublishState( world );
            var newOne = WorldReleaseInfo.Create( buildDate, fixWorkflow, results );
            state.Add( monitor, newOne );

            var publisher = new SimplePublisher( state, packageSender, artifactHandler, versionTag );
            if( await publisher.RunAsync( monitor, cancel ) )
            {
                FixWorkflow.DeleteCurrent( monitor, world );
                return true;
            }
            return false;
        }
    }


    async Task OnRoadmapBuildAsync( IActivityMonitor monitor, RoadmapBuildEventArgs e, CancellationToken cancel )
    {
        if( e.ShouldPublish )
        {
            // "ckli publish", when everything has already been published, may trigger a check of the remote feeds here.
            //
            // if( e.Roadmap.SolutionPublishCount == 0 )
            // {
            //    monitor.Info( $"Checking that remote feeds contain the packages." );
            // }
            // else 
            if( !await PublishAsync( monitor, World, _artifactHandler, _versionTag, e.BuildDate, e.Roadmap, cancel ).ConfigureAwait( false ) )
            {
                e.SetFailed();
            }
        }

        static Task<bool> PublishAsync( IActivityMonitor monitor,
                                        World world,
                                        ArtifactHandlerPlugin artifactHandler,
                                        VersionTagPlugin versionTag,
                                        DateTime buildDate,
                                        Roadmap roadmap,
                                        CancellationToken cancel )
        {
            var packageSender = PackageSender.Create( monitor,
                                                      roadmap.Graph.BranchName.Name,
                                                      roadmap.IsCIBuild,
                                                      artifactHandler,
                                                      world.StackRepository.SecretsStore );
            if( packageSender == null ) return Task.FromResult( false );

            var state = new PublishState( world );
            var newOne = WorldReleaseInfo.Create( monitor, buildDate, roadmap );
            state.Add( monitor, newOne );

            var publisher = new SimplePublisher( state, packageSender, artifactHandler, versionTag );
            return publisher.RunAsync( monitor, cancel );
        }
    }

}
