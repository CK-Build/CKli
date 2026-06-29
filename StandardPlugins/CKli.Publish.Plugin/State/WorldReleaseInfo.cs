using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;

using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Publish.Plugin;

sealed class WorldReleaseInfo
{
    readonly DateTime _buildDate;
    readonly ImmutableArray<RepoPublishInfo> _repos;
    readonly int _publishedLength;
    readonly bool _isCIBuild;
    string _title;

    /// <summary>
    /// Gets the build date of this release.
    /// </summary>
    public DateTime BuildDate => _buildDate;

    /// <summary>
    /// Gets the title.
    /// </summary>
    public string Title => _title;

    /// <summary>
    /// Gets whether this is a build on the "dev/" branch (produces CI packages).
    /// </summary>
    public bool IsCIBuild => _isCIBuild;

    /// <summary>
    /// Gets the published repositories information.
    /// <para>
    /// This is never empty.
    /// </para>
    /// </summary>
    public ImmutableArray<RepoPublishInfo> Repos => _repos;

    /// <summary>
    /// Gets the number of "items" to publish: the sum of the <see cref="Repos"/>'s <see cref="RepoPublishInfo.PublishedLength"/>
    /// plus one for this final set.
    /// </summary>
    public int PublishedLength => _publishedLength;

    WorldReleaseInfo( string title, DateTime buildDate, ImmutableArray<RepoPublishInfo> repos, int publishedLength, bool isCIBuild )
    {
        _title = title;
        _buildDate = buildDate;
        _repos = repos;
        _publishedLength = publishedLength;
        _isCIBuild = isCIBuild;
    }

    /// <summary>
    /// Creates from a <see cref="Roadmap"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="buildDate">The build date to consider.</param>
    /// <param name="roadmap">The built roadmap.</param>
    /// <returns>A new world release.</returns>
    internal static WorldReleaseInfo Create( IActivityMonitor monitor, DateTime buildDate, Roadmap roadmap )
    {
        var repoInfos = new RepoPublishInfo[roadmap.SolutionPublishCount];
        int publishedLength = 0;
        int i = 0;
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( s.MustPublish )
            {
                var (version, tag, content) = s.GetFinalPublishInfo();
                // The branch that will be pushed.
                // We use the RepoPublishInfo.BranchPushRefSpecs here to have an atomic push with all the branches manipulation at once.
                ImmutableArray<string> pushRefSpecs = [];
                string branchName;
                if( roadmap.IsCIBuild )
                {
                    branchName = s.Solution.Branch.BranchName.DevName;
                    // We are publishing a CI: the regular branch MAY be new to the remote when the repository is a brand new one.
                    var regularName = s.Solution.Branch.BranchName.Name;
                    // Defensive programming: the regular branch should exist locally but we don't want to fail here.
                    var b = s.Repo.GitRepository.GetBranch( monitor, regularName, LogLevel.Warn );
                    if( b != null && b.TrackedBranch == null )
                    {
                        // The remote branch has not been found locally: we push it.
                        monitor.Warn( $"Branch '{regularName}' has no tracked branch. Creating branch 'origin/{regularName}'." );
                        b = s.Repo.GitRepository.Repository.Branches.Update( b, u => { u.Remote = "origin"; u.UpstreamBranch = b.CanonicalName; } );
                        pushRefSpecs = [$"{b.CanonicalName}:{b.CanonicalName}"];
                    }
                }
                else
                {
                    branchName = s.Solution.Branch.BranchName.Name;
                    // We are publishing a non-CI: the regular branch will be pushed.
                    // We also suppress its remote "dev/" branch (that has been integrated) by the build.
                    pushRefSpecs = [$":refs/remotes/origin/{s.Solution.Branch.BranchName.DevName}" ];
                }
                var r = new RepoPublishInfo( s.Repo, branchName, i, version, tag, content, pushRefSpecs );
                repoInfos[i++] = r;
                publishedLength += r.PublishedLength;
            }
        }
        Throw.DebugAssert( i == repoInfos.Length );
        return new WorldReleaseInfo( buildDate.ToString( "yyyy.MM.dd+HH.mm" ),
                                     buildDate,
                                     ImmutableCollectionsMarshal.AsImmutableArray( repoInfos ),
                                     publishedLength,
                                     roadmap.IsCIBuild );
    }

    /// <summary>
    /// Creates from a <see cref="FixWorkflow"/>.
    /// </summary>
    /// <param name="buildDate">The build date to consider.</param>
    /// <param name="fixWorkflow">The fix built.</param>
    /// <param name="results">The build results.</param>
    /// <returns>A new world release.</returns>
    internal static WorldReleaseInfo Create( DateTime buildDate, FixWorkflow fixWorkflow, ImmutableArray<BuildResult> results )
    {
        var repoInfos = new RepoPublishInfo[results.Length];
        int publishedLength = 0;
        for( int i = 0; i < results.Length; i++ )
        {
            var b = results[i];
            var r = new RepoPublishInfo( i, fixWorkflow.Targets[i].BranchName, b );
            repoInfos[i++] = r;
            publishedLength += r.PublishedLength;
        }
        return new WorldReleaseInfo( fixWorkflow.ToString(), buildDate, ImmutableCollectionsMarshal.AsImmutableArray( repoInfos ), publishedLength, isCIBuild: false );
    }

}
