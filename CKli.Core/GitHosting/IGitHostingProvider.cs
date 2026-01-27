using CK.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core.GitHosting;

/// <summary>
/// Provides Git hosting API operations for a specific provider instance.
/// Each instance represents a specific server (e.g., github.com, gitea.company.com).
/// Instances are created on-demand by <see cref="ProviderDetector"/>.
/// </summary>
/// <remarks>
/// <para><b>Detection Limitations:</b></para>
/// <list type="bullet">
/// <item><b>Gitea:</b> <c>GET /api/v1/version</c> typically requires authentication.
/// Detection without credentials will fail for most Gitea instances.</item>
/// <item><b>Enterprise/Self-Hosted:</b> Instances behind Cloudflare or reverse
/// proxies may not respond to API sniffing. Use hostnames containing
/// provider hints (e.g., "gitea.company.com") for reliable detection.</item>
/// <item><b>GitLab Self-Hosted:</b> Some instances disable <c>/api/v4/version</c>
/// for unauthenticated requests.</item>
/// <item><b>Fallback:</b> When detection fails, the system tries each provider
/// with available credentials. Requires valid PATs for identification.</item>
/// </list>
/// </remarks>
public interface IGitHostingProvider : IDisposable
{
    /// <summary>
    /// Gets the cloud provider type for this instance.
    /// Returns <see cref="KnownCloudGitProvider.Unknown"/> for self-hosted or enterprise instances.
    /// </summary>
    KnownCloudGitProvider CloudProvider { get; }

    /// <summary>
    /// Gets the unique identifier for this instance (the hostname).
    /// Examples: "github.com", "gitea.company.com", "gitlab.myorg.local"
    /// </summary>
    string InstanceId { get; }

    /// <summary>
    /// Gets the base URL for API calls.
    /// Examples: "https://api.github.com", "https://gitea.company.com/api/v1"
    /// </summary>
    Uri BaseApiUrl { get; }

    /// <summary>
    /// Parses owner and repository name from a remote URL.
    /// </summary>
    /// <param name="remoteUrl">The remote URL to parse (HTTPS or SSH format).</param>
    /// <returns>The owner and repository name, or null if parsing failed.</returns>
    (string Owner, string RepoName)? ParseRemoteUrl( string remoteUrl );

    /// <summary>
    /// Creates a new repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="options">The repository creation options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result containing the created repository info.</returns>
    Task<GitHostingOperationResult<RepositoryInfo>> CreateRepositoryAsync(
        IActivityMonitor monitor,
        RepositoryCreateOptions options,
        CancellationToken ct = default );

    /// <summary>
    /// Archives a repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repoName">The repository name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    Task<GitHostingOperationResult> ArchiveRepositoryAsync(
        IActivityMonitor monitor,
        string owner,
        string repoName,
        CancellationToken ct = default );

    /// <summary>
    /// Deletes a repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repoName">The repository name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    Task<GitHostingOperationResult> DeleteRepositoryAsync(
        IActivityMonitor monitor,
        string owner,
        string repoName,
        CancellationToken ct = default );

    /// <summary>
    /// Gets information about a repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repoName">The repository name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result containing the repository info.</returns>
    Task<GitHostingOperationResult<RepositoryInfo>> GetRepositoryInfoAsync(
        IActivityMonitor monitor,
        string owner,
        string repoName,
        CancellationToken ct = default );
}
