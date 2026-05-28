using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.VersionTag.Plugin;
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
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly VersionTagPlugin _versionTag;

    GitHostingProvider? _hostingProvider;
    NormalizedPath _hostedRepoPath;
    string? _releaseId;

    public SimplePublisher( PublishState state,
                            PackageSender packageSender,
                            ReleaseDatabasePlugin releaseDatabase,
                            ArtifactHandlerPlugin artifactHandler,
                            VersionTagPlugin versionTag )
    {
        _state = state;
        _packageSender = packageSender;
        _releaseDatabase = releaseDatabase;
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
        var branch = r.GetBranch( monitor, repo.BranchName, missingLocalAndRemote: LogLevel.Error );
        if( branch == null )
        {
            return null;
        }

        // Enter the atomic phase:
        // - version tag -> (create draft release -> push build branch with remove remote "dev/" or create remote regular)
        var versionedTag = "v" + repo.PublishVersion.ToString();
        if( !r.PushTags( monitor, [versionedTag] ) )
        {
            return null;
        }

        _releaseId = await CreateDraftReleaseAndPushBranches( monitor, repo, versionedTag, r, branch, cancel ).ConfigureAwait( false );

        if( _releaseId == null )
        {
            // Compensate!
            // Tries to remove the pushed version tag.
            if( !r.DeleteRemoteTags( monitor, [versionedTag] ) )
            {
                monitor.Error( $"""
                    Error while compensating the previous error.
                    The tag '{versionedTag}' has been pushed but should be removed from the 'origin' remote '{r.RepositoryKey.OriginUrl}'.
                    """ );
            }
            return null;
        }
        return _releaseId == null
                ? null
                : _state.ForwardPrimaryCursor( monitor, forwardLength );
    }

    async Task<string?> CreateDraftReleaseAndPushBranches( IActivityMonitor monitor, RepoPublishInfo repo, string versionedTag, GitRepository r, LibGit2Sharp.Branch branch, CancellationToken cancel )
    {
        Throw.DebugAssert( _hostingProvider != null );
        var releaseId = await _hostingProvider.CreateDraftReleaseAsync( monitor, _hostedRepoPath, versionedTag, cancel ).ConfigureAwait( false );
        if( releaseId != null )
        {
            bool isCI = repo.PublishVersion.IsCI();
            // Draft release created. Push the branch(es) now.
            // We use the DeferredPushRefSpecs here to have an atomic push with all the branches manipulation at once.
            if( !isCI )
            {
                // We are publishing a non-CI: the regular branch will be pushed below: we also
                // suppress its remote "dev/" branch (that has been integrated) by the build.
                r.DeferredPushRefSpecs.Add( $":refs/remotes/origin/{BranchName.ToDevBranchName( repo.BranchName )}" );
            }
            else
            {
                // We are publishing a CI: the regular branch MAY be new to the remote when the repository is a brand new one.
                var regularName = BranchName.ToRegularBranchName( repo.BranchName );
                // Defensive programming: the regular branch must exist locally.
                var b = r.GetBranch( monitor, regularName, LogLevel.Warn );
                if( b != null && b.TrackedBranch == null )
                {
                    monitor.Warn( $"Branch '{regularName}' has no tracked branch. Creating branch 'origin/{regularName}'." );
                    b = r.Repository.Branches.Update( b, u => { u.Remote = "origin"; u.UpstreamBranch = b.CanonicalName; } );
                    r.DeferredPushRefSpecs.Add( $"{b.CanonicalName}:{b.CanonicalName}" );
                }
            }
            // Pushes the branch (and may be remove the "dev/" one or push/create the regular one).
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
        // Moves the release from local to published database.
        if( !_releaseDatabase.PublishRelease( monitor, repo.Repo, repo.PublishVersion ) )
        {
            return null;
        }
        // To consider that the war is won, we could ensure that the published database
        // is pushed in the Stack repository... But this is a lot of commits (one for each repository)!
        // And this is not crucial because the published database is just an index (the tag matters),
        // so we postpone the stack push to the end of the world.
        // if( !repo.Repo.World.StackRepository.PushChanges( monitor ) ) return null; 

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
            _versionTag.CleanupLocalRelease( monitor, repo.Repo, repo.PublishVersion, repo.BuildContentInfo, removeFromNuGetGlobalCache: false );
        }
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
