using CK.Core;
using System;
using System.Diagnostics;
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
    private protected GitHostingProvider( string baseUrl,
                                          IGitRepositoryAccessKey gitKey )
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
    /// Creates a new repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="isPrivate">Whether the repository must be private.
    /// When let to null, defaults to the (opposite of) <see cref="IsDefaultPublic"/>.
    /// </param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The created repository info or null on error.</returns>
    public abstract Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                       NormalizedPath repoPath,
                                                                       bool? isPrivate = null,
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
