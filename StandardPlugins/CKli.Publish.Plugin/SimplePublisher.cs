using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

/// <summary>
/// Implements simple, sequential, publication process.
/// </summary>
sealed partial class SimplePublisher
{
    readonly PublishState _state;
    readonly PackageSender _packageSender;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly VersionTagPlugin _versionTag;

    GitHostingProvider? _hostingProvider;
    NormalizedPath _hostedRepoPath;
    string? _releaseId;

    public SimplePublisher( PublishState state,
                            PackageSender packageSender,
                            ArtifactHandlerPlugin artifactHandler,
                            VersionTagPlugin versionTag )
    {
        _state = state;
        _packageSender = packageSender;
        _artifactHandler = artifactHandler;
        _versionTag = versionTag;
    }

    public async Task<bool> RunAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        var step = _state.PrimaryCursor;
        RepoPublishInfo? repo;
        IDisposableGroup? disposableGroup = null;
        while( !step.IsEndOfState )
        {
            switch( step.Location )
            {
                case PublishState.Cursor.LocType.BegOfRepo:
                    repo = _state.PrimaryCursor.Repo;
                    Throw.DebugAssert( repo != null );
                    disposableGroup = monitor.OpenInfo( $"Publishing '{repo.Repo.DisplayPath}' version '{repo.PublishVersion}'." );
                    step = await OnBegOfRepoAsync( monitor, repo, cancel ).ConfigureAwait( false );
                    break;
                case PublishState.Cursor.LocType.InPackage:
                    repo = _state.PrimaryCursor.Repo;
                    Throw.DebugAssert( repo != null );
                    step = await OnInPackagesAsync( monitor, repo, cancel ).ConfigureAwait( false );
                    break;
                case PublishState.Cursor.LocType.InFile:
                    repo = _state.PrimaryCursor.Repo;
                    Throw.DebugAssert( repo != null );
                    step = await OnInFilesAsync( monitor, repo, cancel ).ConfigureAwait( false );
                    break;
                case PublishState.Cursor.LocType.EndOfRepo:
                    repo = _state.PrimaryCursor.Repo;
                    Throw.DebugAssert( repo != null );
                    step = await OnEndOfRepoAsync( monitor, repo, cancel ).ConfigureAwait( false );
                    if( disposableGroup != null )
                    {
                        disposableGroup.Dispose();
                        disposableGroup = null;
                    }
                    break;
                case PublishState.Cursor.LocType.EndOfWorld:
                    step = await OnEndOfWorldAsync( monitor, cancel ).ConfigureAwait( false );
                    break;
            }
            if( step == null ) return false;
        }
        disposableGroup?.Dispose();
        _state.World.StackRepository.PushChanges( monitor );
        return true;
    }

    async Task<PublishState.Cursor?> OnBegOfRepoAsync( IActivityMonitor monitor, RepoPublishInfo repo, CancellationToken cancel )
    {
        if( !repo.Repo.GitRepository.RepositoryKey.TryGetHostingInfo( monitor, out _hostingProvider, out _hostedRepoPath ) )
        {
            monitor.Error( $"Unable to resolve Git hosting provider for '{repo.Repo.DisplayPath}' ({repo.Repo.OriginUrl})." );
            return null;
        }
        // If we have packages to push, we let the InPackages state create the release.
        // But if we have no packages, we create the release right now.
        return repo.BuildContentInfo.Produced.Length == 0
                    ? await CreateReleaseAsync( monitor, repo, 1, cancel ).ConfigureAwait( false )
                    : _state.ForwardPrimaryCursor( monitor, 1 );
    }

    async Task<PublishState.Cursor?> OnInPackagesAsync( IActivityMonitor monitor, RepoPublishInfo repo, CancellationToken cancel )
    {
        var toPush = repo.BuildContentInfo.Produced;
        using( monitor.OpenTrace( $"Pushing '{toPush.Concatenate("', '")}' packages." ) )
        {
            if( !await _packageSender.SendAsync( monitor, repo.PublishVersion, toPush, cancel ).ConfigureAwait( false ) )
            {
                return null;
            }
        }
        return await CreateReleaseAsync( monitor, repo, toPush.Length, cancel ).ConfigureAwait( false );
    }

    async Task<PublishState.Cursor?> CreateReleaseAsync( IActivityMonitor monitor, RepoPublishInfo repo, int forwardLength, CancellationToken cancel )
    {
        GitRepository r = repo.Repo.GitRepository;
        // To create a release, hosting providers (like GitHub) require that the tag exists in the repository, so it's time to push it.
        // The build branch name must obviously exist.
        var branch = r.GetBranch( monitor, repo.BranchName, missingLocalAndRemote: CK.Core.LogLevel.Error );
        if( branch == null )
        {
            return null;
        }

        // Enter the atomic phase:
        // - version tag -> (create draft release -> push build branch with removed remote "dev/" or create the remote regular branch).
        var tag = repo.PublishTag;
        Throw.DebugAssert( repo.PublishVersion.IsLocal() == tag.CanonicalName.StartsWith( "refs/tags/local/", System.StringComparison.Ordinal ) );
        if( repo.PublishVersion.IsLocal() )
        {
            r.Repository.Tags.Remove( tag.CanonicalName );
            tag = r.Repository.ApplyTag( $"v{repo.PublishVersion}",
                                         repo.PublishTag.Target.Sha,
                                         repo.PublishTag.Annotation.Tagger,
                                         repo.BuildContentInfo.ToString() );
        }
        if( !r.PushTags( monitor, [tag.CanonicalName] ) )
        {
            return null;
        }

        _releaseId = await CreateDraftReleaseAndPushBranches( monitor, repo, tag.FriendlyName, r, branch, cancel ).ConfigureAwait( false );

        if( _releaseId == null )
        {
            // Compensate!
            // Tries to remove the pushed version tag.
            if( !r.DeleteRemoteTags( monitor, [tag.CanonicalName] ) )
            {
                monitor.Error( $"""
                    Error while compensating the previous error.
                    The tag '{tag.CanonicalName}' has been pushed but should be removed from the 'origin' remote '{r.RepositoryKey.OriginUrl}'.
                    """ );
            }
            return null;
        }
        return _releaseId == null
                ? null
                : _state.ForwardPrimaryCursor( monitor, forwardLength );
    }

    async Task<string?> CreateDraftReleaseAndPushBranches( IActivityMonitor monitor,
                                                           RepoPublishInfo repo,
                                                           string versionedTag,
                                                           GitRepository r,
                                                           Branch branch,
                                                           CancellationToken cancel )
    {
        Throw.DebugAssert( _hostingProvider != null );
        var releaseId = await _hostingProvider.CreateDraftReleaseAsync( monitor, _hostedRepoPath, versionedTag, cancel ).ConfigureAwait( false );
        if( releaseId != null )
        {
            // Draft release created. Push the branch(es) now.
            r.DeferredPushRefSpecs.AddRange( repo.BranchPushRefSpecs );
            // Pushes the branch and the deferred ref specs (this may remove the "dev/" or push/create the regular branch).
            if( !r.PushBranch( monitor, branch, autoCreateRemoteBranch: true ) )
            {
                // TODO:
                // Compensate!
                // Delete the draft release.
                // await _hostingProvider.DeleteDraftReleaseAsync( monitor, _hostedRepoPath, releaseId, cancel ).ConfigureAwait( false );
                return null;
            }
        }
        return releaseId;
    }

    async Task<PublishState.Cursor?> OnInFilesAsync( IActivityMonitor monitor, RepoPublishInfo repo, CancellationToken cancel )
    {
        Throw.DebugAssert( _hostingProvider != null && _releaseId != null );
        Throw.DebugAssert( repo.BuildContentInfo.AssetFileNames.Length > 0 );
        var folder = _artifactHandler.GetAssetsFolder( repo.Repo, repo.PublishVersion );
        if( !Directory.Exists( folder ) )
        {
            monitor.Error( $"""
                Expected folder '{folder}' to exist with files:
                '{repo.BuildContentInfo.AssetFileNames.Concatenate("', '")}'.
                """ );
            return null;
        }
        if( !await _hostingProvider.AddReleaseAssetsAsync( monitor, _hostedRepoPath, _releaseId, folder, cancel ).ConfigureAwait( false ) )
        {
            return null;
        }
        return _state.ForwardPrimaryCursor( monitor, repo.BuildContentInfo.AssetFileNames.Length );
    }

    async Task<PublishState.Cursor?> OnEndOfRepoAsync( IActivityMonitor monitor, RepoPublishInfo repo, CancellationToken cancel )
    {
        // We are almost done: finalize the hosted release.
        Throw.DebugAssert( _hostingProvider != null && _releaseId != null );
        if( !await _hostingProvider.FinalizeReleaseAsync( monitor, _hostedRepoPath, _releaseId, cancel ).ConfigureAwait( false ) )
        {
            return null; 
        }
        // Resets the hosting provider and release state.
        _hostingProvider = null;
        _releaseId = null;
        // Housekeeping: if the cleanup fails, we still consider this release done.
        // Trick here for the tests, we don't cleanup the $Local when testing: we want to keep the versions
        // that a build has produced.
        if( !CKliRootEnv.DefaultCKliEnv.CurrentDirectory.Path.Contains( "CK/.PublicStack/CK-Plugins/Tests/Plugins.Tests" ) )
        {
            _artifactHandler.DestroyLocalRelease( monitor, repo.Repo, repo.PublishVersion, repo.BuildContentInfo, removeFromNuGetGlobalCache: false );
        }
        monitor.Info( ScreenType.CKliScreenTag, $"Published {repo.BuildContentInfo.Produced.Length} packages of '{repo.Repo.DisplayPath}/{repo.PublishVersion}'." );
        return _state.ForwardPrimaryCursor( monitor, 1 );
    }

    async Task<PublishState.Cursor?> OnEndOfWorldAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        var world = _state.PrimaryCursor.World;
        Throw.DebugAssert( world != null );
        monitor.Info( $"Published '{world.Title}'." );
        return _state.ForwardPrimaryCursor( monitor, 1 );
    }

}
