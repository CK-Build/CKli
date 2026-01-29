using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CK.Core;
using LibGit2Sharp;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitHub hosting provider implementation.
/// Supports github.com and GitHub Enterprise instances.
/// </summary>
sealed partial class GitHubProvider : GitHostingProvider
{
    public GitHubProvider( string baseUrl, KnownCloudGitProvider cloudGitProvider, IGitRepositoryAccessKey gitKey )
        : base( baseUrl, cloudGitProvider, gitKey )
    {
    }

    public override bool CanArchiveRepository => true;

    public override async Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor, NormalizedPath repoPath, CancellationToken ct = default )
    {
        if( !CheckRepoNameAndWriteAccess( monitor, repoPath, out var creds ) )
        {
            return false;
        }

        throw new NotImplementedException();
    }


    public override async Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                             NormalizedPath repoPath,
                                                                             HostedRepositoryCreateOptions? options = null,
                                                                             CancellationToken ct = default )
    {
        if( !CheckRepoNameAndWriteAccess( monitor, repoPath, out var creds ) )
        {
            return null;
        }

        throw new NotImplementedException();
    }

    public override async Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor,
                                                            NormalizedPath repoPath,
                                                            CancellationToken ct = default )
    {
        if( !CheckRepoNameAndWriteAccess( monitor, repoPath, out var creds ) )
        {
            return false;
        }

        throw new NotImplementedException();
    }

    public override async Task<HostedRepositoryInfo?> GetRepositoryInfoAsync( IActivityMonitor monitor,
                                                                              NormalizedPath repoPath,
                                                                              CancellationToken ct = default )
    {
        if( !CheckValidRepoPath( monitor, repoPath ) )
        {
            return null;
        }
        if( !GitKey.GetReadCredentials( monitor, out var creds ) )
        {
            return null;
        }

        throw new NotImplementedException();
    }

    bool CheckRepoNameAndWriteAccess( IActivityMonitor monitor,
                                      NormalizedPath repoPath,
                                      [NotNullWhen(true)]out UsernamePasswordCredentials? creds )
    {
        if( !CheckValidRepoPath( monitor, repoPath ) )
        {
            creds = null;
            return false;
        }
        return GitKey.GetWriteCredentials( monitor, out creds ) )
    }

    static bool CheckValidRepoPath( IActivityMonitor monitor, NormalizedPath repoPath )
    {
        if( repoPath.Parts.Count != 2 )
        {
            monitor.Error( $"Invalid GitHub repository path '{repoPath}'. Must be '<owner>/<name>'." );
            return false;
        }
        return true;
    }
}

//{
//    readonly string _hostName;
//    readonly Uri _baseApiUrl;
//    readonly ISecretsStore _secretsStore;
//    readonly Func<IActivityMonitor, GitHubClient?> _clientFactory;
//    GitHubClient? _client;

//    /// <summary>
//    /// Creates a GitHub provider for a specific instance.
//    /// </summary>
//    /// <param name="hostName">The host name.</param>
//    /// <param name="baseApiUrl">The base API URL.</param>
//    /// <param name="secretsStore">The secrets store for PAT retrieval.</param>
//    public GitHubProvider( string hostName, Uri baseApiUrl, ISecretsStore secretsStore )
//    {
//        ArgumentException.ThrowIfNullOrWhiteSpace( hostName );
//        ArgumentNullException.ThrowIfNull( baseApiUrl );
//        ArgumentNullException.ThrowIfNull( secretsStore );

//        _hostName = hostName;
//        _baseApiUrl = baseApiUrl;
//        _secretsStore = secretsStore;

//        _clientFactory = monitor =>
//        {
//            var patKey = GetWritePatKey();
//            var pat = _secretsStore.TryGetRequiredSecret( monitor, patKey );
//            return pat != null ? new GitHubClient( pat, _baseApiUrl ) : null;
//        };
//    }

//    /// <summary>
//    /// Creates a GitHub provider for testing with a pre-configured client.
//    /// </summary>
//    /// <param name="pat">The Personal Access Token.</param>
//    /// <param name="httpMessageHandler">Optional HTTP message handler for testing.</param>
//    /// <param name="instanceId">Optional instance ID (default: github.com).</param>
//    public GitHubProvider( string pat, HttpMessageHandler? httpMessageHandler = null, string? instanceId = null )
//    {
//        ArgumentException.ThrowIfNullOrWhiteSpace( pat );

//        _hostName = instanceId ?? "github.com";
//        // Ensure trailing slash for proper relative URL resolution
//        _baseApiUrl = _hostName == "github.com"
//            ? new Uri( "https://api.github.com/" )
//            : new Uri( $"https://{_hostName}/api/v3/" );
//        _secretsStore = null!; // Not used in test mode
//        _client = new GitHubClient( pat, _baseApiUrl, httpMessageHandler );
//        _clientFactory = _ => _client;
//    }

//    /// <inheritdoc />
//    public KnownCloudGitProvider CloudProvider =>
//        string.Equals( _hostName, "github.com", StringComparison.OrdinalIgnoreCase )
//            ? KnownCloudGitProvider.GitHub
//            : KnownCloudGitProvider.Unknown;

//    /// <inheritdoc />
//    public string HostName => _hostName;

//    /// <inheritdoc />
//    public Uri BaseApiUrl => _baseApiUrl;

//    /// <summary>
//    /// Repositories can be archived on any GitHub.
//    /// </summary>
//    public bool CanArchiveRepository => true;

//    /// <inheritdoc />
//    public (string Owner, string RepoName)? ParseRemoteUrl( string remoteUrl )
//    {
//        var normalized = RemoteUrlParser.TryNormalizeToHttps( remoteUrl );
//        if( normalized == null ) return null;

//        if( !string.Equals( normalized.Host, _hostName, StringComparison.OrdinalIgnoreCase ) )
//            return null;

//        return RemoteUrlParser.ParseStandardPath( normalized.AbsolutePath );
//    }

//    /// <inheritdoc />
//    public async Task<GitHostingOperationResult<HostedRepositoryInfo>> CreateRepositoryAsync(
//        IActivityMonitor monitor,
//        HostedRepositoryCreateOptions options,
//        CancellationToken ct = default )
//    {
//        var client = GetClient( monitor );
//        if( client == null )
//        {
//            return GitHostingOperationResult<HostedRepositoryInfo>.Fail( $"No GitHub PAT available. Set {GetWritePatKey()} in secrets." );
//        }

//        return await client.CreateRepositoryAsync( monitor, options, ct );
//    }

//    /// <inheritdoc />
//    public async Task<GitHostingOperationResult> ArchiveRepositoryAsync(
//        IActivityMonitor monitor,
//        string owner,
//        string repoName,
//        CancellationToken ct = default )
//    {
//        var client = GetClient( monitor );
//        if( client == null )
//        {
//            return GitHostingOperationResult.Fail( $"No GitHub PAT available. Set {GetWritePatKey()} in secrets." );
//        }

//        return await client.ArchiveRepositoryAsync( monitor, owner, repoName, ct );
//    }

//    /// <inheritdoc />
//    public async Task<GitHostingOperationResult> DeleteRepositoryAsync(
//        IActivityMonitor monitor,
//        string owner,
//        string repoName,
//        CancellationToken ct = default )
//    {
//        var client = GetClient( monitor );
//        if( client == null )
//        {
//            return GitHostingOperationResult.Fail( $"No GitHub PAT available. Set {GetWritePatKey()} in secrets." );
//        }

//        return await client.DeleteRepositoryAsync( monitor, owner, repoName, ct );
//    }

//    /// <inheritdoc />
//    public async Task<GitHostingOperationResult<HostedRepositoryInfo>> GetRepositoryInfoAsync(
//        IActivityMonitor monitor,
//        string owner,
//        string repoName,
//        CancellationToken ct = default )
//    {
//        var client = GetClient( monitor );
//        if( client == null )
//        {
//            return GitHostingOperationResult<HostedRepositoryInfo>.Fail( $"No GitHub PAT available. Set {GetWritePatKey()} in secrets." );
//        }

//        return await client.GetRepositoryAsync( monitor, owner, repoName, ct );
//    }

//    GitHubClient? GetClient( IActivityMonitor monitor )
//    {
//        return _client ?? _clientFactory( monitor );
//    }


//    /// <inheritdoc />
//    public void Dispose()
//    {
//        _client?.Dispose();
//    }
//}
