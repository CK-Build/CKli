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

    public RoadmapPublisher( PackageSender packageSender,
                             ArtifactHandlerPlugin artifactHandler,
                             BranchModelPlugin branchModel,
                             bool keepLocalReleaseAfterPublish,
                             DefaultBranchFailures defaultBranchFailures )
        : base( packageSender, artifactHandler, branchModel.BranchNamespace.Root.Name, keepLocalReleaseAfterPublish, defaultBranchFailures )
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
        // We have everything we need.
        var branch = _branches.FindRequired( monitor, version );
        if( branch == null ) return Task.FromResult( false );

        bool isCI = version.IsCI;
        var repo = solution.Repo.GitRepository;
        // A CI version is built from the "dev/" branch when it exists, from the regular one otherwise (a "--ci.0"
        // is typically built on the regular branch tip). The branch to push is the one the version comes from.
        bool fromDevBranch = isCI && repo.GetBranch( monitor, branch.DevName, missingLocalAndRemote: LogLevel.None ) != null;
        string gitBranchName = fromDevBranch ? branch.DevName : branch.Name;

        Tag? fakeTagVersionToPush = null;

        // If the BaseVersion is a +fake, it must be pushed so others can understand (and correctly work in CI builds).
        // Only once the actual non-CI stable version is published can the +fake tag be deleted.
        // => We push the tag except when we are building a non-CI stable version.
        if( !version.IsStable || isCI )
        {
            var baseTagCommit = buildInfo.VersionInfo.HotZone.LastStable;
            fakeTagVersionToPush = baseTagCommit.IsFakeVersion ? baseTagCommit.Tag : baseTagCommit.FakeVersion?.Tag;
        }
        // A CI version published from the "dev/" branch pushes its base too when the remote doesn't have it; a non-CI
        // version integrates the "dev/" branch, that is removed from the remote.
        string? baseBranchName = fromDevBranch ? branch.Name : null;
        string? branchToRemove = isCI ? null : branch.DevName;
        return PublishCoreAsync( monitor,
                                 solution.Repo,
                                 gitBranchName,
                                 baseBranchName,
                                 ImmutableArray<string>.Empty,
                                 branchToRemove,
                                 version,
                                 tag,
                                 fakeTagVersionToPush,
                                 content,
                                 cancellation );
    }
}
