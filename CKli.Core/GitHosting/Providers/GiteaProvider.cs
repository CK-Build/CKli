using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CK.Core;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// Gitea hosting provider implementation.
/// Gitea is self-hosted only - there is no official cloud instance.
/// </summary>
/// <remarks>
/// <para><b>PAT Key Resolution:</b></para>
/// <para>Since Gitea is always self-hosted, PAT keys follow the hostname pattern:</para>
/// <list type="bullet">
/// <item><c>gitea.company.com</c> → <c>GITEA_COMPANY_COM_GIT_WRITE_PAT</c></item>
/// <item><c>git.internal.org</c> → <c>GIT_INTERNAL_ORG_GIT_WRITE_PAT</c></item>
/// </list>
/// <para>Operations require a PAT with appropriate repository permissions (admin for create/delete).</para>
/// <para><b>Detection Limitations:</b></para>
/// <para>Gitea's version endpoint (<c>GET /api/v1/version</c>) typically requires authentication,
/// making unauthenticated detection unreliable. Detection relies on hostname patterns
/// (e.g., hostnames containing "gitea") or the try-all fallback mechanism.</para>
/// </remarks>
public sealed partial class GiteaProvider : IGitHostingProvider
{
    readonly string _instanceId;
    readonly Uri _baseApiUrl;
    readonly ISecretsStore _secretsStore;
    readonly Func<IActivityMonitor, GiteaClient?> _clientFactory;
    GiteaClient? _client;

    /// <summary>
    /// Creates a Gitea provider for a specific instance.
    /// </summary>
    /// <param name="instanceId">The instance identifier (hostname).</param>
    /// <param name="baseApiUrl">The base API URL.</param>
    /// <param name="secretsStore">The secrets store for PAT retrieval.</param>
    public GiteaProvider( string instanceId, Uri baseApiUrl, ISecretsStore secretsStore )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( instanceId );
        ArgumentNullException.ThrowIfNull( baseApiUrl );
        ArgumentNullException.ThrowIfNull( secretsStore );

        _instanceId = instanceId;
        // Ensure trailing slash for proper relative URL resolution
        _baseApiUrl = baseApiUrl.ToString().EndsWith( '/' )
            ? baseApiUrl
            : new Uri( baseApiUrl.ToString() + "/" );
        _secretsStore = secretsStore;

        _clientFactory = monitor =>
        {
            var patKey = GetWritePatKey();
            var pat = _secretsStore.TryGetRequiredSecret( monitor, patKey );
            return pat != null ? new GiteaClient( pat, _baseApiUrl ) : null;
        };
    }

    /// <summary>
    /// Creates a Gitea provider for testing with a pre-configured client.
    /// </summary>
    /// <param name="pat">The Personal Access Token.</param>
    /// <param name="baseApiUrl">The base API URL.</param>
    /// <param name="httpMessageHandler">Optional HTTP message handler for testing.</param>
    /// <param name="instanceId">Optional instance ID (defaults to the API URL host).</param>
    public GiteaProvider( string pat, Uri baseApiUrl, HttpMessageHandler? httpMessageHandler = null, string? instanceId = null )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( pat );
        ArgumentNullException.ThrowIfNull( baseApiUrl );

        _instanceId = instanceId ?? baseApiUrl.Host;
        // Ensure trailing slash for proper relative URL resolution
        _baseApiUrl = baseApiUrl.ToString().EndsWith( '/' )
            ? baseApiUrl
            : new Uri( baseApiUrl.ToString() + "/" );
        _secretsStore = null!; // Not used in test mode
        _client = new GiteaClient( pat, _baseApiUrl, httpMessageHandler );
        _clientFactory = _ => _client;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always returns <see cref="KnownCloudGitProvider.Unknown"/> since Gitea
    /// has no official cloud offering - all instances are self-hosted.
    /// </remarks>
    public KnownCloudGitProvider CloudProvider => KnownCloudGitProvider.Unknown;

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
            return GitHostingOperationResult<RepositoryInfo>.Fail( $"No Gitea PAT available. Set {GetWritePatKey()} in secrets." );
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
            return GitHostingOperationResult.Fail( $"No Gitea PAT available. Set {GetWritePatKey()} in secrets." );
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
            return GitHostingOperationResult.Fail( $"No Gitea PAT available. Set {GetWritePatKey()} in secrets." );
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
            return GitHostingOperationResult<RepositoryInfo>.Fail( $"No Gitea PAT available. Set {GetWritePatKey()} in secrets." );
        }

        return await client.GetRepositoryAsync( monitor, owner, repoName, ct );
    }

    GiteaClient? GetClient( IActivityMonitor monitor )
    {
        return _client ?? _clientFactory( monitor );
    }

    string GetWritePatKey()
    {
        // Gitea is always self-hosted: sanitize hostname for use as env var prefix
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
