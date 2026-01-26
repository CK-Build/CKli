using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CK.Core;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitLab hosting provider implementation.
/// Supports gitlab.com and self-hosted GitLab instances.
/// </summary>
/// <remarks>
/// <para><b>PAT Key Resolution:</b></para>
/// <list type="bullet">
/// <item>For <c>gitlab.com</c>: <c>GITLAB_GIT_WRITE_PAT</c></item>
/// <item>For self-hosted (e.g., <c>gitlab.company.com</c>): <c>GITLAB_COMPANY_COM_GIT_WRITE_PAT</c></item>
/// </list>
/// <para>Operations require a PAT with appropriate project permissions (maintainer or owner for create/delete).</para>
/// </remarks>
public sealed partial class GitLabProvider : IGitHostingProvider
{
    readonly string _instanceId;
    readonly Uri _baseApiUrl;
    readonly ISecretsStore _secretsStore;
    readonly Func<IActivityMonitor, GitLabClient?> _clientFactory;
    GitLabClient? _client;

    /// <summary>
    /// Creates a GitLab provider for a specific instance.
    /// </summary>
    /// <param name="instanceId">The instance identifier (hostname).</param>
    /// <param name="baseApiUrl">The base API URL.</param>
    /// <param name="secretsStore">The secrets store for PAT retrieval.</param>
    public GitLabProvider( string instanceId, Uri baseApiUrl, ISecretsStore secretsStore )
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
            return pat != null ? new GitLabClient( pat, _baseApiUrl ) : null;
        };
    }

    /// <summary>
    /// Creates a GitLab provider for testing with a pre-configured client.
    /// </summary>
    /// <param name="pat">The Personal Access Token.</param>
    /// <param name="httpMessageHandler">Optional HTTP message handler for testing.</param>
    /// <param name="instanceId">Optional instance ID (default: gitlab.com).</param>
    public GitLabProvider( string pat, HttpMessageHandler? httpMessageHandler = null, string? instanceId = null )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( pat );

        _instanceId = instanceId ?? "gitlab.com";
        _baseApiUrl = _instanceId == "gitlab.com"
            ? new Uri( "https://gitlab.com/api/v4/" )
            : new Uri( $"https://{_instanceId}/api/v4/" );
        _secretsStore = null!; // Not used in test mode
        _client = new GitLabClient( pat, _baseApiUrl, httpMessageHandler );
        _clientFactory = _ => _client;
    }

    /// <inheritdoc />
    public KnownCloudGitProvider CloudProvider =>
        string.Equals( _instanceId, "gitlab.com", StringComparison.OrdinalIgnoreCase )
            ? KnownCloudGitProvider.GitLab
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

        // GitLab supports nested groups: /group/subgroup/repo -> owner="group/subgroup", repo="repo"
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
            return GitHostingOperationResult<RepositoryInfo>.Fail( $"No GitLab PAT available. Set {GetWritePatKey()} in secrets." );
        }

        return await client.CreateProjectAsync( monitor, options, ct );
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
            return GitHostingOperationResult.Fail( $"No GitLab PAT available. Set {GetWritePatKey()} in secrets." );
        }

        return await client.ArchiveProjectAsync( monitor, owner, repoName, ct );
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
            return GitHostingOperationResult.Fail( $"No GitLab PAT available. Set {GetWritePatKey()} in secrets." );
        }

        return await client.DeleteProjectAsync( monitor, owner, repoName, ct );
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
            return GitHostingOperationResult<RepositoryInfo>.Fail( $"No GitLab PAT available. Set {GetWritePatKey()} in secrets." );
        }

        return await client.GetProjectAsync( monitor, owner, repoName, ct );
    }

    GitLabClient? GetClient( IActivityMonitor monitor )
    {
        return _client ?? _clientFactory( monitor );
    }

    string GetWritePatKey()
    {
        if( string.Equals( _instanceId, "gitlab.com", StringComparison.OrdinalIgnoreCase ) )
        {
            return "GITLAB_GIT_WRITE_PAT";
        }
        // For self-hosted GitLab: sanitize hostname for use as env var prefix
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
