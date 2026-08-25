using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using LibGit2Sharp;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Publish.Plugin;

/// <summary>
/// Carries the list of <see cref="RepoInfo"/> and supports a <see cref="Cursor"/> that flattens the
/// content to publish. This must can be created for a Fix workflow (<see cref="Create(FixWorkflow, ImmutableArray{BuildResult})"/>)
/// or a roadmap (<see cref="Create(IActivityMonitor, Roadmap)"/>) after a successful build (<see cref="Roadmap.BuildSuccess"/> mus be true). 
/// </summary>
sealed partial class DirectPublisher
{
    readonly World _world;
    readonly ImmutableArray<RepoInfo> _repos;
    readonly int _publishedLength;
    Cursor _primaryCursor;

    /// <summary>
    /// Gets the world.
    /// </summary>
    public World World => _world;

    /// <summary>
    /// Gets the list of repositories to publish.
    /// </summary>
    public ImmutableArray<RepoInfo> Repos => _repos;

    /// <summary>
    /// Gets the cursor associated to this state.
    /// </summary>
    public Cursor PrimaryCursor => _primaryCursor;

    /// <summary>
    /// Updates the <see cref="PrimaryCursor"/> by forwarding it.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="offset">The number of positions. Must be positive.</param>
    /// <returns>The updated <see cref="PrimaryCursor"/>.</returns>
    public Cursor ForwardPrimaryCursor( IActivityMonitor monitor, int offset )
    {
        Throw.CheckArgument( offset > 0 );
        return _primaryCursor = _primaryCursor.Forward( offset );
    }

    /// <summary>
    /// Creates a new <see cref="Cursor"/> positioned at the start of this state.
    /// </summary>
    /// <returns>The cursor to use to traverse this state.</returns>
    public Cursor CreateCursor( int position = 0 ) => Cursor.Create( this );

    internal DirectPublisher( World world, ImmutableArray<RepoInfo> repos, int publishedLength )
    {
        _world = world;
        _repos = repos;
        _publishedLength = publishedLength;
        _primaryCursor = Cursor.Create( this );
    }

    /// <summary>
    /// Creates a <see cref="DirectPublisher"/> from a <see cref="FixWorkflow"/>.
    /// </summary>
    /// <param name="fixWorkflow">The fix built.</param>
    /// <param name="results">The build results.</param>
    /// <returns>A new world release.</returns>
    internal static DirectPublisher Create( FixWorkflow fixWorkflow, ImmutableArray<BuildResult> results )
    {
        var repoInfos = new RepoInfo[results.Length];
        int publishedLength = 0;
        for( int i = 0; i < results.Length; i++ )
        {
            var b = results[i];
            var r = new RepoInfo( i, fixWorkflow.Targets[i].BranchName, b );
            repoInfos[i] = r;
            publishedLength += r.PublishedLength;
        }
        return new DirectPublisher( fixWorkflow.World, ImmutableCollectionsMarshal.AsImmutableArray( repoInfos ), publishedLength );
    }

    /// <summary>
    /// Creates a <see cref="DirectPublisher"/> from a <see cref="Roadmap"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="roadmap">The built roadmap.</param>
    /// <returns>A publisher.</returns>
    internal static DirectPublisher Create( IActivityMonitor monitor, Roadmap roadmap )
    {
        Throw.DebugAssert( roadmap.DirectPublishCount > 0 && roadmap.BuildSuccess is true );
        var repoInfos = new RepoInfo[roadmap.DirectPublishCount];
        int publishedLength = 0;
        int i = 0;
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( s.PublishableStatus is PublishableStatus.Build or PublishableStatus.PublishRequired )
            {
                Throw.DebugAssert( "Otherwise we won't be PublishableStatus.Build or PublishRequired.", s.BuildInfo != null );
                Throw.CheckState( "A successful build must have been done before.", !s.MustBuild || s.BuildInfo.BuildResult != null );

                SVersion version;
                Tag tag;
                BuildContentInfo content;

                if( s.MustBuild )
                {
                    var r = s.BuildInfo.BuildResult;
                    Throw.DebugAssert( r != null );
                    version = r.Version;
                    tag = r.VersionTag;
                    content = r.Content;
                }
                else
                {
                    version = s.LastBuild.Version;
                    tag = s.LastBuild.Tag;
                    Throw.DebugAssert( "If this was a +fake, the MustBuild would have been true.", s.LastBuild.TagCommit.BuildContentInfo != null );
                    content = s.LastBuild.TagCommit.BuildContentInfo;
                }
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
                    pushRefSpecs = [$":refs/heads/{s.Solution.Branch.BranchName.DevName}"];
                }
                var info = new RepoInfo( s.Repo, branchName, i, version, tag, content, pushRefSpecs );
                repoInfos[i++] = info;
                publishedLength += info.PublishedLength;
            }
        }
        Throw.DebugAssert( i == repoInfos.Length );
        return new DirectPublisher( repoInfos[0].Repo.World, ImmutableCollectionsMarshal.AsImmutableArray( repoInfos ), publishedLength );
    }

}

