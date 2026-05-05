using CK.Core;
using LibGit2Sharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Provides Git hosting API operations. This is a base class for all hosting providers.
/// <para>
/// File system (with LibGit2Sharp), GitHub, GitLab and Gitea support is currently available.
/// </para>
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public abstract partial class GitHostingProvider
{
    readonly string _baseUrl;
    readonly IGitRepositoryAccessKey _gitKey;
    string? _hostingType;

    /// <summary>
    /// Initializes a new provider.
    /// </summary>
    /// <param name="baseUrl">The <see cref="BaseUrl"/>.</param>
    /// <param name="gitKey">The git key.</param>
    private protected GitHostingProvider( string baseUrl, IGitRepositoryAccessKey gitKey )
    {
        _baseUrl = baseUrl;
        _gitKey = gitKey;
    }

    /// <summary>
    /// Gets the type of this provider.
    /// </summary>
    public string ProviderType => _hostingType ??= GetType().Name;

    /// <summary>
    /// Gets whether this provider handles public repositories by default.
    /// <para>
    /// This is based on the first <see cref="GitRepositoryKey"/> used to resolve this provider.
    /// </para>
    /// </summary>
    public bool IsDefaultPublic => _gitKey.IsPublic ?? true;

    /// <summary>
    /// Gets the base url. For url based providers, this is based on the <see cref="UriPartial.Authority"/>,
    /// for file system provider, this is only the "file://" scheme.
    /// <para>
    /// Examples: "https://github.com" (for GitHub cloud),
    /// "https://gitea.company.com:3712".
    /// </para>
    /// </summary>
    public string BaseUrl => _baseUrl;

    /// <summary>
    /// Gets the <see cref="IGitRepositoryAccessKey"/> that:
    /// <list type="number">
    ///     <item>Identifies this provider.</item>
    ///     <item>Is used by this provider to resolve required secrets.</item>
    /// </list>
    /// </summary>
    public IGitRepositoryAccessKey GitKey => _gitKey;

    /// <summary>
    /// Gets the normalized repository path that corresponds to a <see cref="GitRepositoryKey.OriginUrl"/>.
    /// The key has resolved this provider as its hosting provider.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="key">The repository key.</param>
    /// <returns>The path to the repository or <see cref="NormalizedPath.IsEmptyPath"/> if it cannot be resolved.</returns>
    internal protected abstract NormalizedPath GetRepositoryPathFromUrl( IActivityMonitor monitor, GitRepositoryKey key );

    /// <summary>
    /// Gets information about a repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="mustExist">
    /// True to emit an error and return null if the <paramref name="repoPath"/> doesn't exist.
    /// False to obtain a result with a false <see cref="HostedRepositoryInfo.Exists"/>.
    /// </param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The result containing the repository info or null on error.</returns>
    public abstract Task<HostedRepositoryInfo?> GetRepositoryInfoAsync( IActivityMonitor monitor,
                                                                        NormalizedPath repoPath,
                                                                        bool mustExist,
                                                                        CancellationToken cancellation = default );

    /// <summary>
    /// Creates a new repository. Whether an initial empty commit is created or not depends on the implementation.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="isPrivate">Whether the repository must be private.
    /// When let to null, defaults to the (opposite of) <see cref="IsDefaultPublic"/>.
    /// </param>
    /// <param name="defaultBranchName">Default branch name to consider.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The created repository info or null on error.</returns>
    public abstract Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                       NormalizedPath repoPath,
                                                                       bool? isPrivate = null,
                                                                       string defaultBranchName = "main",
                                                                       CancellationToken cancellation = default );

    /// <summary>
    /// Gets whether this provider is able to archive a repository.
    /// Not all providers have this capability (file system provider doesn't).
    /// </summary>
    public abstract bool CanArchiveRepository { get; }

    /// <summary>
    /// Archives a repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="archive">True to archive, false to unarchive.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public abstract Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor,
                                                       NormalizedPath repoPath,
                                                       bool archive,
                                                       CancellationToken cancellation = default );

    /// <summary>
    /// Creates a release on the specified <paramref name="versionedTag"/> that must exist in remote (the tag must have
    /// already been pushed).
    /// <para>
    /// This always considers that the created release is a draft (even if the provider doesn't support this notion): the release
    /// is considered mutable (<see cref="AddReleaseAssetsAsync(IActivityMonitor, NormalizedPath, string, NormalizedPath, CancellationToken)"/>
    /// can be called on the returned release identifier).
    /// </para>
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="versionedTag">The versioned tag. It must exist in the repository (no control is made).</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>On success, a non null release identifier that must be used to associate asset files to the release. Null on error.</returns>
    public abstract Task<string?> CreateDraftReleaseAsync( IActivityMonitor monitor,
                                                           NormalizedPath repoPath,
                                                           string versionedTag,
                                                           CancellationToken cancellation = default );

    /// <summary>
    /// Adds all files in a folder to the assets associated to a release.
    /// <para>
    /// This is a default implementation that basically iterates on the files
    /// and calls <see cref="AddReleaseAssetAsync(IActivityMonitor, NormalizedPath, string, NormalizedPath, string?, CancellationToken)"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="releaseIdentifier">The release identifier returned by <see cref="CreateDraftReleaseAsync"/>.</param>
    /// <param name="assetsFolder">The local folder with the assets files.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on success, false otherwise.</returns>
    public virtual async Task<bool> AddReleaseAssetsAsync( IActivityMonitor monitor,
                                                           NormalizedPath repoPath,
                                                           string releaseIdentifier,
                                                           NormalizedPath assetsFolder,
                                                           CancellationToken cancellation = default )
    {
        foreach( var f in Directory.GetFiles( assetsFolder ) )
        {
            if( !await AddReleaseAssetAsync( monitor, repoPath, releaseIdentifier, f, fileName:null, cancellation  ).ConfigureAwait( false )  )
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Adds a file to the assets associated to a release.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="releaseIdentifier">The release identifier returned by <see cref="CreateDraftReleaseAsync"/>.</param>
    /// <param name="filePath">The local file path.</param>
    /// <param name="fileName">The file name defaults to the <see cref="NormalizedPath.LastPart"/> of the <paramref name="filePath"/>.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on success, false otherwise.</returns>
    public abstract Task<bool> AddReleaseAssetAsync( IActivityMonitor monitor,
                                                     NormalizedPath repoPath,
                                                     string releaseIdentifier,
                                                     NormalizedPath filePath,
                                                     string? fileName = null,
                                                     CancellationToken cancellation = default );

    /// <summary>
    /// Locks a release previously created with <see cref="CreateDraftReleaseAsync(IActivityMonitor, NormalizedPath, string, CancellationToken)"/>.
    /// <para>
    /// By default this simply returns true: this is the default implementation when the hosting provider doesn't support draft releases.
    /// </para>
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="releaseIdentifier">The release identifier returned by <see cref="CreateDraftReleaseAsync"/>.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on success, false otherwise.</returns>
    public virtual Task<bool> FinalizeReleaseAsync( IActivityMonitor monitor,
                                                    NormalizedPath repoPath,
                                                    string releaseIdentifier,
                                                    CancellationToken cancellation = default )
    {
        return Task.FromResult( true );
    }

    /// <summary>
    /// Deletes a repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public abstract Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor,
                                                      NormalizedPath repoPath,
                                                      CancellationToken cancellation = default );

    /// <summary>
    /// Returns this <see cref="ProviderType"/> and its <see cref="GitKey"/>.
    /// </summary>
    /// <returns>This provider readable name.</returns>
    public sealed override string ToString() => $"{ProviderType} - {GitKey}";

}
