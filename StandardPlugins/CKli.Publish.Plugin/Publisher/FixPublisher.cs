using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using LibGit2Sharp;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

/// <summary>
/// Fix Workflow publisher. Fix releases live on their own explicit <c>fix/vMajor.Minor</c> branch
/// which is not resolvable from the version through the World's branch namespace.
/// No "dev/" branch cleanup, defensive push, or additional tag applies here.
/// </summary>
sealed class FixPublisher : BasePublisher
{
    public FixPublisher( PackageSender packageSender,
                         ArtifactHandlerPlugin artifactHandler,
                         string rootBranchName,
                         bool keepLocalReleaseAfterPublish )
        : base( packageSender, artifactHandler, rootBranchName, keepLocalReleaseAfterPublish )
    {
    }

    /// <summary>
    /// Publishes <paramref name="repo"/>'s <paramref name="version"/> on an explicit
    /// <paramref name="branchName"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository to publish.</param>
    /// <param name="branchName">The fix branch to push.</param>
    /// <param name="version">The version to publish.</param>
    /// <param name="tag">The tag that carries the version.</param>
    /// <param name="content">The content (packages and asset files) to publish.</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>True on success, false on error (logged).</returns>
    public Task<bool> PublishAsync( IActivityMonitor monitor,
                                    Repo repo,
                                    string branchName,
                                    SVersion version,
                                    Tag tag,
                                    BuildContentInfo content,
                                    CancellationToken cancel )
    {
        return PublishCoreAsync( monitor, repo, branchName, ImmutableArray<string>.Empty, null, version, tag, null, content, cancel );
    }
}
