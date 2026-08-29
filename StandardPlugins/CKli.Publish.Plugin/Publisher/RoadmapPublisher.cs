using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using LibGit2Sharp;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Publish.Plugin;

/// <summary>
/// Regular, roadmap-driven publisher. The target Git branch is resolved from the version through the
/// World's <see cref="BranchNamespace"/> (the "dev/" branch for a CI build, the regular one otherwise).
/// </summary>
sealed class RoadmapPublisher : BasePublisher
{
    readonly BranchNamespace _branches;

    public RoadmapPublisher( PackageSender packageSender, ArtifactHandlerPlugin artifactHandler, BranchModelPlugin branchModel )
        : base( packageSender, artifactHandler )
    {
        _branches = branchModel.BranchNamespace;
    }

    /// <summary>
    /// Publishes a <see cref="Roadmap.BuildSolution"/> whose <see cref="Roadmap.BuildSolution.PublishableStatus"/> is
    /// <see cref="PublishableStatus.Build"/> or <see cref="PublishableStatus.PublishRequired"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="solution">The build solution to publish.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on success, false on error (logged).</returns>
    public Task<bool> PublishAsync( IActivityMonitor monitor, Roadmap.BuildSolution solution, CancellationToken cancellation )
    {
        Throw.CheckArgument( solution.PublishableStatus is PublishableStatus.Build or PublishableStatus.PublishRequired );
        Throw.CheckState( "A successful build must have been done before.", !solution.MustBuild || solution.BuildInfo.BuildResult != null );

        var buildInfo = solution.BuildInfo;
        
        SVersion version;
        Tag tag;
        BuildContentInfo content;
        if( buildInfo.MustBuild )
        {
            var r = buildInfo.BuildResult;
            Throw.DebugAssert( r != null );
            version = r.Version;
            tag = r.VersionTag;
            content = r.Content;
        }
        else
        {
            var lastBuild = buildInfo.Solution.LastBuild;
            version = lastBuild.Version;
            tag = lastBuild.Tag;
            Throw.DebugAssert( "If this was a +fake, the MustBuild would have been true.", lastBuild.TagCommit.BuildContentInfo != null );
            content = lastBuild.TagCommit.BuildContentInfo;
        }
        // If the BaseVersion is a +fake, it must be pushed so others can understand (and correctly work in CI builds).
        // Only once the actual non-CI stable version is published can the +fake tag be deleted.
        // => We push the tag except when we are building a non-CI stable version.
        Tag? fakeTagVersionToPush = null;
        if( !version.IsStable || version.IsCI )
        {
            var baseTagCommit = buildInfo.VersionInfo.HotZone.LastStable;
            fakeTagVersionToPush = baseTagCommit.IsFakeVersion ? baseTagCommit.Tag : baseTagCommit.FakeVersion?.Tag;
        }
        // We have everything we need.
        var branch = _branches.FindRequired( monitor, version );
        if( branch == null ) return Task.FromResult( false );

        bool isCI = version.IsCI;
        string gitBranchName = isCI ? branch.DevName : branch.Name;

        var pushRefSpecs = ComputeMainLinePushRefSpecs( monitor, solution.Repo, branch, isCI );

        return PublishCoreAsync( monitor, solution.Repo, gitBranchName, pushRefSpecs, version, tag, fakeTagVersionToPush, content, cancellation );
    }

    // Defensive fix for a brand new repository: on a CI build, the regular (non "dev/") branch may not
    // have been pushed to the remote yet. If so, push it along with the "dev/" branch.
    static ImmutableArray<string> ComputeMainLinePushRefSpecs( IActivityMonitor monitor, Repo repo, BranchName branch, bool isCI )
    {
        if( !isCI )
        {
            // The regular branch will be pushed.
            // Its remote "dev/" branch (now integrated) is removed.
            return [$":refs/heads/{branch.DevName}"];
        }
        // CI build: ensures that the "dev/" branch is tracked.
        var r = repo.GitRepository;
        var b = r.GetBranch( monitor, branch.Name, missingLocalAndRemote: LogLevel.Warn );
        if( b != null && b.TrackedBranch == null )
        {
            monitor.Warn( $"Branch '{branch.Name}' has no tracked branch. Creating branch 'origin/{branch.Name}'." );
            b = r.Repository.Branches.Update( b, u => { u.Remote = "origin"; u.UpstreamBranch = b.CanonicalName; } );
            return [$"{b.CanonicalName}:{b.CanonicalName}"];
        }
        return ImmutableArray<string>.Empty;
    }
}
