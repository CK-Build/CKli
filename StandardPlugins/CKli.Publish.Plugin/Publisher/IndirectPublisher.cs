using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.VersionTag.Plugin;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

/// <summary>
/// Publisher of the <see cref="PublishedProfile.RequiredPublications"/>: the "local/" releases that a roadmap's
/// publication depends on but that live on another branch (<see cref="PublishableStatus.IndirectPublishRequired"/>).
/// The target Git branch is resolved from the version through the World's <see cref="BranchNamespace"/>, exactly as
/// <see cref="RoadmapPublisher"/> does.
/// <para>
/// Unlike <see cref="RoadmapPublisher"/>, no branch integration applies: only the packages and the version tag are
/// pushed. These releases belong to a branch that the current operation is not working on, so removing its remote
/// "dev/" branch or pushing its main line would be a side effect nobody asked for.
/// </para>
/// </summary>
sealed class IndirectPublisher : BasePublisher
{
    readonly BranchNamespace _branches;

    public IndirectPublisher( PackageSender packageSender,
                              ArtifactHandlerPlugin artifactHandler,
                              BranchModelPlugin branchModel,
                              bool keepLocalReleaseAfterPublish )
        : base( packageSender, artifactHandler, branchModel.BranchNamespace.Root.Name, keepLocalReleaseAfterPublish )
    {
        _branches = branchModel.BranchNamespace;
    }

    /// <summary>
    /// Publishes a "local/" release on the branch that owns its version.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="release">The release to publish.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on success, false on error (logged).</returns>
    public Task<bool> PublishAsync( IActivityMonitor monitor, RepoReleaseInfo release, CancellationToken cancellation )
    {
        Throw.CheckArgument( release.Version.IsLocal() );

        var branch = _branches.FindRequired( monitor, release.Version );
        if( branch == null ) return Task.FromResult( false );

        string gitBranchName = release.Version.IsCI ? branch.DevName : branch.Name;
        return PublishCoreAsync( monitor,
                                 release.Repo,
                                 gitBranchName,
                                 ImmutableArray<string>.Empty,
                                 release.Version,
                                 release.TagCommit.Tag,
                                 null,
                                 release.Content,
                                 cancellation );
    }
}
