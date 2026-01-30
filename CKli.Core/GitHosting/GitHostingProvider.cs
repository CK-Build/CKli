using CK.Core;
using CKli.Core.GitHosting.Providers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Provides Git hosting API operations.
/// <para>
/// File system (with LibGit2Sharp), GitHub, GitLab and Gitea support is available
/// by design but plugins can bring support for other hosts.
/// </para>
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public abstract partial class GitHostingProvider
{
    readonly string _baseUrl;
    readonly KnownCloudGitProvider _cloudGitProvider;
    readonly IGitRepositoryAccessKey _gitKey;
    string? _hostingType;

    // Instantiated by factory methods.
    static Dictionary<(string,bool), GitHostingProvider?>? _providers;

    private protected GitHostingProvider( string baseUrl,
                                          KnownCloudGitProvider cloudGitProvider,
                                          IGitRepositoryAccessKey gitKey )
    {
        _baseUrl = baseUrl;
        _cloudGitProvider = cloudGitProvider;
        _gitKey = gitKey;
    }

    /// <summary>
    /// Gets the cloud provider type for this instance.
    /// Returns <see cref="KnownCloudGitProvider.Unknown"/> for self-hosted or enterprise instances.
    /// </summary>
    public KnownCloudGitProvider CloudProvider => _cloudGitProvider;

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
    public bool IsDefaultPublic => _gitKey.IsPublic;

    /// <summary>
    /// Gets the base url. For url based providers, this is based on the <see cref="UriPartial.Authority"/>,
    /// for <see cref="KnownCloudGitProvider.FileSystem"/> this is only the "file://" scheme.
    /// <para>
    /// Examples: "https://github.com" (for <see cref="KnownCloudGitProvider.GitHub"/>),
    /// "https://gitea.company.com:3712".
    /// </para>
    /// </summary>
    public string BaseUrl => _baseUrl;

    /// <summary>
    /// Gets the <see cref="IGitRepositoryAccessKey"/> used by this provider.
    /// </summary>
    public IGitRepositoryAccessKey GitKey => _gitKey;

    /// <summary>
    /// Gets whether this provider is able to archive a repository.
    /// Not all providers have this capability (<see cref="KnownCloudGitProvider.FileSystem"/> doesn't).
    /// </summary>
    public abstract bool CanArchiveRepository { get; }

    /// <summary>
    /// Creates a new repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="options">The repository creation options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created repository info or null on error.</returns>
    public abstract Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                       NormalizedPath repoPath,
                                                                       HostedRepositoryCreateOptions? options = null,
                                                                       CancellationToken ct = default );

    /// <summary>
    /// Archives a repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public abstract Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor,
                                                       NormalizedPath repoPath,
                                                       CancellationToken ct = default );

    /// <summary>
    /// Deletes a repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public abstract Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor,
                                                      NormalizedPath repoPath,
                                                      CancellationToken ct = default );

    /// <summary>
    /// Gets information about a repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result containing the repository info or null on error.</returns>
    public abstract Task<HostedRepositoryInfo?> GetRepositoryInfoAsync( IActivityMonitor monitor,
                                                                        NormalizedPath repoPath,
                                                                        CancellationToken ct = default );

    /// <summary>
    /// Returns this <see cref="ProviderType"/> and its <see cref="GitKey"/>.
    /// </summary>
    /// <returns>This provider readable name.</returns>
    public sealed override string ToString() => $"{ProviderType} - {GitKey}";

}
