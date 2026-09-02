using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Publish.Plugin;

/// <summary>
/// Shared publishing mechanics for a single <see cref="Repo"/>'s release: pushes the version tag
/// (and an optional additional tag), the produced NuGet packages, creates and populates the hosted
/// release, and pushes the target Git branch.
/// <para>
/// Concrete publishers only differ in how the target Git branch (and its push ref specs) is
/// determined: <see cref="RoadmapPublisher"/> resolves it from the version through the World's branch
/// namespace, <see cref="FixPublisher"/> uses an explicit Fix Workflow branch.
/// </para>
/// </summary>
abstract class BasePublisher
{
    readonly PackageSender _packageSender;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly bool _keepLocalReleaseAfterPublish;

    protected BasePublisher( PackageSender packageSender,
                             ArtifactHandlerPlugin artifactHandler,
                             bool keepLocalReleaseAfterPublish )
    {
        _packageSender = packageSender;
        _artifactHandler = artifactHandler;
        _keepLocalReleaseAfterPublish = keepLocalReleaseAfterPublish;
    }

    /// <summary>
    /// Publishes a single repository's release once the target branch (and its push ref specs) and
    /// any additional tag to push have been resolved by the concrete publisher.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository to publish.</param>
    /// <param name="branchName">The Git branch to push.</param>
    /// <param name="branchPushRefSpecs">Additional ref specs to push along with <paramref name="branchName"/>.</param>
    /// <param name="version">The version to publish.</param>
    /// <param name="tag">The tag that carries the version.</param>
    /// <param name="extraTagToPush">An optional additional tag that must also be pushed to the remote.</param>
    /// <param name="content">The content (packages and asset files) to publish.</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>True on success, false on error (logged).</returns>
    protected async Task<bool> PublishCoreAsync( IActivityMonitor monitor,
                                                 Repo repo,
                                                 string branchName,
                                                 ImmutableArray<string> branchPushRefSpecs,
                                                 SVersion version,
                                                 Tag tag,
                                                 Tag? extraTagToPush,
                                                 BuildContentInfo content,
                                                 CancellationToken cancel )
    {
        using var group = monitor.OpenInfo( $"Publishing '{repo.DisplayPath}' version '{version}'." );

        if( !repo.GitRepository.RepositoryKey.TryGetHostingInfo( monitor, out var hostingProvider, out var hostedRepoPath ) )
        {
            monitor.Error( $"Unable to resolve Git hosting provider for '{repo.DisplayPath}' ({repo.OriginUrl})." );
            return false;
        }

        if( !_artifactHandler.HasAllArtifacts( monitor, repo, version, content, out var assetsFolder ) )
        {
            return false;
        }

        GitRepository r = repo.GitRepository;
        var branch = r.GetBranch( monitor, branchName, missingLocalAndRemote: LogLevel.Error );
        if( branch == null )
        {
            return false;
        }

        if( content.Produced.Length > 0 )
        {
            using( monitor.OpenTrace( $"Pushing '{content.Produced.Concatenate( "', '" )}' packages." ) )
            {
                if( !await _packageSender.SendAsync( monitor, version, content.Produced, cancel ).ConfigureAwait( false ) )
                {
                    return false;
                }
            }
        }

        // Enter the atomic phase: version tag (+ optional extra tag) -> push branch (with the deferred
        // ref specs) -> create draft release. To create a release, hosting providers (like GitHub) require
        // the tag to exist in the repository, so it's pushed first.
        //
        // The branch is pushed before the release is created: on the very first publication of a new
        // repository, the remote would otherwise hold nothing but tags, a state GitHub intermittently
        // reports as "Git Repository is empty." and that makes every release call fail. Pushing the branch
        // first doesn't publish anything more (the tag push already made the commit public), it only leaves
        // the branch pushed when the release cannot be created.
        bool isLocalVersion = version.IsLocal();
        Throw.DebugAssert( "We may be on an already published version.", isLocalVersion || !version.IsBuilding() );
        if( isLocalVersion )
        {
            r.Repository.Tags.Remove( tag.CanonicalName );
            tag = r.Repository.ApplyTag( $"v{version}", tag.Target.Sha, tag.Annotation.Tagger, content.ToString() );
        }
        string[] tagNames = extraTagToPush == null
                                ? [tag.CanonicalName]
                                : [tag.CanonicalName, extraTagToPush.CanonicalName];
        if( !r.PushTags( monitor, tagNames ) )
        {
            return false;
        }

        r.DeferredPushRefSpecs.AddRange( branchPushRefSpecs );
        if( !r.PushBranch( monitor, branch, autoCreateRemoteBranch: true ) )
        {
            // Compensate! Tries to remove the pushed version tag.
            UnpublishTag( monitor, tag, r, isLocalVersion );
            return false;
        }

        var releaseId = await hostingProvider.CreateDraftReleaseAsync( monitor, hostedRepoPath, tag.FriendlyName, cancel ).ConfigureAwait( false );
        if( releaseId == null )
        {
            // Compensate! Tries to remove the pushed version tag. The pushed branch is kept: the commit is
            // public since the tag has been pushed, unpushing the branch would gain nothing.
            UnpublishTag( monitor, tag, r, isLocalVersion );
            return false;
        }

        if( !assetsFolder.IsEmptyPath && !await hostingProvider.AddReleaseAssetsAsync( monitor, hostedRepoPath, releaseId, assetsFolder, cancel ).ConfigureAwait( false ) )
        {
            // Compensate! Tries to remove the pushed version tag and deletes the draft release.
            await DeleteDraftAsync( monitor, tag, hostingProvider, hostedRepoPath, r, releaseId, isLocalVersion, cancel ).ConfigureAwait( false );
            return false;
        }

        if( !await hostingProvider.FinalizeReleaseAsync( monitor, hostedRepoPath, releaseId, cancel ).ConfigureAwait( false ) )
        {
            // Compensate! Tries to remove the pushed version tag and deletes the draft release.
            await DeleteDraftAsync( monitor, tag, hostingProvider, hostedRepoPath, r, releaseId, isLocalVersion, cancel ).ConfigureAwait( false );
            return false;
        }

        // Housekeeping: the local release is useless now that the packages are published, unless
        // PublishPlugin.KeepLocalReleaseAfterPublish asks to keep it (test harnesses do, so that the
        // version a build has produced remains available to the following commands).
        if( !_keepLocalReleaseAfterPublish )
        {
            // If the cleanup fails, we still consider this release done.
            _artifactHandler.DestroyLocalRelease( monitor, repo, version, content, removeFromNuGetGlobalCache: false );
        }
        monitor.Info( ScreenType.CKliScreenTag, $"Published {content.Produced.Length} packages of '{repo.DisplayPath}/{version}'." );
        return true;
    }

    static void UnpublishTag( IActivityMonitor monitor, Tag tag, GitRepository r, bool wasLocalVersion )
    {
        if( wasLocalVersion )
        {
            bool success;
            Exception? ex = null;
            try
            {
                // Exact inverse of the promotion above (Tags.Remove of the "local/" then ApplyTag of the
                // version): the LOCAL version tag must be removed too, not only the remote one. Forgetting
                // it leaves the repository with BOTH "v{version}" and "local/v{version}" on the same commit,
                // a state that claims the version is published when it is not.
                // Capture the annotation before removing the ref: the tag object is only reachable through
                // it until the ref is recreated.
                var sha = tag.Target.Sha;
                var tagger = tag.Annotation.Tagger;
                var message = tag.Annotation.Message;
                success = r.DeleteRemoteTags( monitor, [tag.CanonicalName] );
                r.Repository.Tags.Remove( tag.CanonicalName );
                r.Repository.ApplyTag( $"local/{tag.FriendlyName}", sha, tagger, message );
            }
            catch( Exception e )
            {
                ex = e;
                success = false;
            }
            if( !success )
            {
                monitor.Error( $"""
                    Error while compensating the previous error.
                    The tag '{tag.CanonicalName}' must be deleted (locally AND on the remote) and must be
                    recreated locally with a 'local/' prefix: 'local/{tag.FriendlyName}'.
                    """, ex );
            }
        }
    }

    static Task DeleteDraftAsync( IActivityMonitor monitor, Tag tag, GitHostingProvider hostingProvider, NormalizedPath hostedRepoPath, GitRepository r, string releaseId, bool wasLocalVersion, CancellationToken cancel )
    {
        UnpublishTag( monitor, tag, r, wasLocalVersion );
        return hostingProvider.DeleteReleaseAsync( monitor, hostedRepoPath, releaseId, cancel );
    }
}
