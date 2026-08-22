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
                          VersionTagPlugin versionTag )
        : base( primaryContext )
    {
        _build = build;
        _artifactHandler = artifactHandler;
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
                                     _versionTag,
                                     e.BuildDate,
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
                                              VersionTagPlugin versionTag,
                                              DateTime buildDate,
                                              FixWorkflow fixWorkflow,
                                              bool ciBuild,
                                              bool keepBranchOnSuccessfulPublish,
                                              ImmutableArray<BuildResult> results,
                                              CancellationToken cancel )
        {
            // A fix is on the stable branch.
            var packageSender = PackageSender.Create( monitor,
                                                      prereleaseName: "",
                                                      ciBuild,
                                                      artifactHandler,
                                                      world.StackRepository.SecretsStore );
            if( packageSender == null ) return false;

            var state = new PublishState( world );
            var newOne = WorldReleaseInfo.Create( buildDate, fixWorkflow, results );
            state.Add( monitor, newOne );

            var publisher = new SimplePublisher( state, packageSender, artifactHandler, versionTag );
            if( await publisher.RunAsync( monitor, cancel ) )
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
                        foreach( var p in newOne.Repos )
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


    async Task OnRoadmapBuildAsync( IActivityMonitor monitor, RoadmapBuildEventArgs e, CancellationToken cancel )
    {
        if( e.ShouldPublish )
        {
            var roadmap = e.Roadmap;
            Throw.DebugAssert( roadmap.PublishableStatus > PublishableStatus.None );
            if( roadmap.PublishableStatus > PublishableStatus.AlreadyPublished )
            {
                //var publish = PublishRoadmap.Create( e.Monitor, roadmap, _versionTag );
                //if( publish != null )
                //{
                //    e.Screen.Display( publish.ToRenderable );
                //    if( !roadmap.DryRun )
                //    {
                //        await publish.PublishAsync( e.Monitor );
                //    }
                //}

                if( !await PublishAsync( monitor, World, _artifactHandler, _versionTag, e.BuildDate, e.Roadmap, cancel ).ConfigureAwait( false ) )
                {
                    e.SetFailed();
                }
            }
        }

        [Obsolete( "Should use the more complex PublishRoadmap now..." )]
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
