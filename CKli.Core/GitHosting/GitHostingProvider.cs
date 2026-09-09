using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Provides Git hosting API operations. This is a base class for all hosting providers.
/// <para>
/// File system (with LibGit2Sharp), GitHub, GitLab and Gitea support are currently available.
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
    /// Reads the content of a file from a repository without cloning it.
    /// <para>
    /// A missing file is not an error: this returns <c>(true,null)</c> and logs at <paramref name="notFoundLogLevel"/>.
    /// Providers differ in what they can distinguish here - a missing file, a missing <paramref name="refName"/>
    /// and a missing repository may all answer the same <c>(true,null)</c> - so this must not be used to
    /// probe for a repository's existence: <see cref="GetRepositoryInfoAsync"/> does that.
    /// </para>
    /// <para>
    /// The content is returned as bytes rather than as a string: the callers of this parse utf-8 Json (a
    /// <c>PublishedIndex</c>, a <c>PublishedProfile</c>) and a decoding step would only be undone.
    /// </para>
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="filePath">The path of the file in the repository ("Published/index.json").</param>
    /// <param name="refName">
    /// The branch, tag or commit to read the file from. Defaults to null: the repository's default branch
    /// (its HEAD when <see cref="HasDefaultBranch"/> is false).
    /// </param>
    /// <param name="notFoundLogLevel">The log level to use when the file doesn't exist.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>Whether the call succeeded, and the file content if it exists (null otherwise or on error).</returns>
    public abstract Task<(bool Success, byte[]? Content)> GetFileContentAsync( IActivityMonitor monitor,
                                                                               NormalizedPath repoPath,
                                                                               NormalizedPath filePath,
                                                                               string? refName = null,
                                                                               LogLevel notFoundLogLevel = LogLevel.Trace,
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
    /// Gets whether this provider supports the notion of "default branch".
    /// When false, <see cref="HostedRepositoryInfo.DefaultBranch"/> is always null
    /// and <see cref="SetDefaultBranchAsync"/> throws an <see cref="InvalidOperationException"/>.
    /// </summary>
    public bool HasDefaultBranch { get; init; }

    /// <summary>
    /// Sets the default branch of a repository. The branch must exist in the repository.
    /// <para>
    /// This must be idempotent: setting the branch that is already the default one is a no-op success.
    /// </para>
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="branchName">The branch name that must become the default one.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    /// <exception cref="InvalidOperationException">When <see cref="HasDefaultBranch"/> is false.</exception>
    public abstract Task<bool> SetDefaultBranchAsync( IActivityMonitor monitor,
                                                      NormalizedPath repoPath,
                                                      string branchName,
                                                      CancellationToken cancellation = default );

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
    /// Gets the releases for the specified repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="pageNumber">The 1-based page number to retrieve.</param>
    /// <param name="countPerPage">Number of items per page.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>List of published releases, or null on error.</returns>
    public abstract Task<List<PublishedReleaseInfo>?> GetReleaseListAsync( IActivityMonitor monitor,
                                                                           NormalizedPath repoPath,
                                                                           int pageNumber = 1,
                                                                           int countPerPage = 100,
                                                                           CancellationToken cancellation = default );

    /// <summary>
    /// Gets the <see cref="PublishedReleaseInfo"/> of a given <paramref name="releaseId"/>.
    /// <para>
    /// This returns null if the release doesn't exist but <paramref name="notFoundLevel"/> enables
    /// to to 
    /// </para>
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="releaseId">The release identifier.</param>
    /// <param name="notFoundLevel">The log level to use when the release doesn't exist.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>Whether the call succeed and the published release info if found, null otherwise or on error.</returns>
    public abstract Task<(bool Success, PublishedReleaseInfo? Info)> GetReleaseAsync( IActivityMonitor monitor,
                                                                                      NormalizedPath repoPath,
                                                                                      string releaseId,
                                                                                      LogLevel notFoundLevel = LogLevel.Trace,
                                                                                      CancellationToken cancellation = default );

    /// <summary>
    /// Deletes a <see cref="PublishedReleaseInfo"/>. This must be idempotent (deleting an unexisting release is a no-op).
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="releaseId">The release identifier.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>True on success (or if the release didn't exist), false on error.</returns>
    public abstract Task<bool> DeleteReleaseAsync( IActivityMonitor monitor,
                                                   NormalizedPath repoPath,
                                                   string releaseId,
                                                   CancellationToken cancellation = default );

    /// <summary>
    /// Creates a release on the specified <paramref name="versionedTag"/> that must exist in remote (the tag must have
    /// already been pushed).
    /// <para>
    /// This always considers that the created release is a draft (even if the provider doesn't support this notion): the release
    /// is considered mutable (<see cref="AddReleaseAssetsAsync(IActivityMonitor, NormalizedPath, string, NormalizedPath, CancellationToken)"/>
    /// can be called on the returned release identifier).
    /// </para>
    /// <para>
    /// This must fail (and an error must be logged) when the release (identified by the <paramref name="versionedTag"/>) already exists.
    /// </para>
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="versionedTag">The versioned tag. It must exist in the repository (no control is made).</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>
    /// On success, a non null release identifier that must be used to associate asset files to the release, get the <see cref="PublishedReleaseInfo"/>
    /// or delete the release.
    /// Null on error.
    /// </returns>
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
    /// Deletes a repository. This must be idempotent (deleting an unexisting repository is a no-op).
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on success (or if the repository didn't exist), false on error.</returns>
    public abstract Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor,
                                                      NormalizedPath repoPath,
                                                      CancellationToken cancellation = default );

    /// <summary>
    /// Returns this <see cref="ProviderType"/> and its <see cref="GitKey"/>.
    /// </summary>
    /// <returns>This provider readable name.</returns>
    public sealed override string ToString() => $"{ProviderType} - {GitKey}";

}
