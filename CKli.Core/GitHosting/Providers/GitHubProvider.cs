using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CK.Core;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitHub hosting provider implementation.
/// Supports github.com and GitHub Enterprise instances.
/// </summary>
/// <remarks>
/// <para><b>PAT Key Resolution:</b></para>
/// <list type="bullet">
/// <item>For <c>github.com</c>: <c>GITHUB_GIT_WRITE_PAT</c></item>
/// <item>For GitHub Enterprise (e.g., <c>github.company.com</c>): <c>GITHUB_COMPANY_COM_GIT_WRITE_PAT</c></item>
/// </list>
/// <para>Operations require a PAT with appropriate repository permissions (admin for create/delete).</para>
/// </remarks>
public sealed partial class GitHubProvider : IGitHostingProvider
{
    readonly string _instanceId;
    readonly Uri _baseApiUrl;
    readonly ISecretsStore _secretsStore;
    readonly Func<IActivityMonitor, GitHubClient?> _clientFactory;
    GitHubClient? _client;

    /// <summary>
    /// Creates a GitHub provider for a specific instance.
    /// </summary>
    /// <param name="instanceId">The instance identifier (hostname).</param>
    /// <param name="baseApiUrl">The base API URL.</param>
    /// <param name="secretsStore">The secrets store for PAT retrieval.</param>
    public GitHubProvider( string instanceId, Uri baseApiUrl, ISecretsStore secretsStore )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( instanceId );
        ArgumentNullException.ThrowIfNull( baseApiUrl );
        ArgumentNullException.ThrowIfNull( secretsStore );

        _instanceId = instanceId;
        _baseApiUrl = baseApiUrl;
        _secretsStore = secretsStore;

        _clientFactory = monitor =>
        {
            var patKey = GetWritePatKey();
            var pat = _secretsStore.TryGetRequiredSecret( monitor, patKey );
            return pat != null ? new GitHubClient( pat, _baseApiUrl ) : null;
        };
    }

    /// <summary>
    /// Creates a GitHub provider for testing with a pre-configured client.
    /// </summary>
    /// <param name="pat">The Personal Access Token.</param>
    /// <param name="httpMessageHandler">Optional HTTP message handler for testing.</param>
    /// <param name="instanceId">Optional instance ID (default: github.com).</param>
    public GitHubProvider( string pat, HttpMessageHandler? httpMessageHandler = null, string? instanceId = null )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( pat );

        _instanceId = instanceId ?? "github.com";
        // Ensure trailing slash for proper relative URL resolution
        _baseApiUrl = _instanceId == "github.com"
            ? new Uri( "https://api.github.com/" )
            : new Uri( $"https://{_instanceId}/api/v3/" );
        _secretsStore = null!; // Not used in test mode
        _client = new GitHubClient( pat, _baseApiUrl, httpMessageHandler );
        _clientFactory = _ => _client;
    }

    /// <inheritdoc />
    public KnownCloudGitProvider CloudProvider =>
        string.Equals( _instanceId, "github.com", StringComparison.OrdinalIgnoreCase )
            ? KnownCloudGitProvider.GitHub
            : KnownCloudGitProvider.Unknown;

    /// <inheritdoc />
    public string InstanceId => _instanceId;

    /// <inheritdoc />
    public Uri BaseApiUrl => _baseApiUrl;

    /// <inheritdoc />
    public (string Owner, string RepoName)? ParseRemoteUrl( string remoteUrl )
    {
        var normalized = RemoteUrlParser.TryNormalizeToHttps( remoteUrl );
        if( normalized == null ) return null;

        if( !string.Equals( normalized.Host, _instanceId, StringComparison.OrdinalIgnoreCase ) )
            return null;

        return RemoteUrlParser.ParseStandardPath( normalized.AbsolutePath );
    }

    /// <inheritdoc />
    public async Task<GitHostingOperationResult<RepositoryInfo>> CreateRepositoryAsync(
        IActivityMonitor monitor,
        RepositoryCreateOptions options,
        CancellationToken ct = default )
    {
        var client = GetClient( monitor );
        if( client == null )
        {
            return GitHostingOperationResult<RepositoryInfo>.Fail( $"No GitHub PAT available. Set {GetWritePatKey()} in secrets." );
        }

        return await client.CreateRepositoryAsync( monitor, options, ct );
    }

    /// <inheritdoc />
    public async Task<GitHostingOperationResult> ArchiveRepositoryAsync(
        IActivityMonitor monitor,
        string owner,
        string repoName,
        CancellationToken ct = default )
    {
        var client = GetClient( monitor );
        if( client == null )
        {
            return GitHostingOperationResult.Fail( $"No GitHub PAT available. Set {GetWritePatKey()} in secrets." );
        }

        return await client.ArchiveRepositoryAsync( monitor, owner, repoName, ct );
    }

    /// <inheritdoc />
    public async Task<GitHostingOperationResult> DeleteRepositoryAsync(
        IActivityMonitor monitor,
        string owner,
        string repoName,
        CancellationToken ct = default )
    {
        var client = GetClient( monitor );
        if( client == null )
        {
            return GitHostingOperationResult.Fail( $"No GitHub PAT available. Set {GetWritePatKey()} in secrets." );
        }

        return await client.DeleteRepositoryAsync( monitor, owner, repoName, ct );
    }

    /// <inheritdoc />
    public async Task<GitHostingOperationResult<RepositoryInfo>> GetRepositoryInfoAsync(
        IActivityMonitor monitor,
        string owner,
        string repoName,
        CancellationToken ct = default )
    {
        var client = GetClient( monitor );
        if( client == null )
        {
            return GitHostingOperationResult<RepositoryInfo>.Fail( $"No GitHub PAT available. Set {GetWritePatKey()} in secrets." );
        }

        return await client.GetRepositoryAsync( monitor, owner, repoName, ct );
    }

    GitHubClient? GetClient( IActivityMonitor monitor )
    {
        return _client ?? _clientFactory( monitor );
    }

    string GetWritePatKey()
    {
        if( string.Equals( _instanceId, "github.com", StringComparison.OrdinalIgnoreCase ) )
        {
            return "GITHUB_GIT_WRITE_PAT";
        }
        // For GitHub Enterprise: sanitize hostname for use as env var prefix
        // Replace non-alphanumeric with _, uppercase, add _GIT_WRITE_PAT suffix
        var sanitized = BadPATChars().Replace( _instanceId, "_" ).ToUpperInvariant();
        return sanitized + "_GIT_WRITE_PAT";
    }

    [GeneratedRegex( "[^A-Za-z_0-9]" )]
    private static partial Regex BadPATChars();

    /// <inheritdoc />
    public void Dispose()
    {
        _client?.Dispose();
    }
}
