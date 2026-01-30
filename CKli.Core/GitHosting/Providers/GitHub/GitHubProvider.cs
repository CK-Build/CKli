using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using CK.Core;
using LibGit2Sharp;

namespace CKli.Core.GitHosting.Providers;


/// <summary>
/// GitHub hosting provider implementation.
/// Supports github.com and GitHub Enterprise instances.
/// </summary>
sealed partial class GitHubProvider : HttpGitHostingProvider
{
    /// <summary>
    /// Constructor for the cloud https://github.com.
    /// </summary>
    /// <param name="gitKey">The git key to use.</param>
    public GitHubProvider( IGitRepositoryAccessKey gitKey )
        : base( "https://github.com", KnownCloudGitProvider.GitHub, gitKey, new Uri( "https://api.github.com/" ) )
    {
    }

    /// <summary>
    /// Constructor for a GitHub server.
    /// </summary>
    /// <param name="baseUrl">The <see cref="HttpGitHostingProvider.BaseApiUrl"/>.</param>
    /// <param name="gitKey">The git key to use.</param>
    /// <param name="authority">
    /// The authority: currently UriComponents.UserInfo | UriComponents.Host | UriComponents.Port
    /// but this may change.
    /// </param>
    public GitHubProvider( string baseUrl, IGitRepositoryAccessKey gitKey, string authority )
        : base( baseUrl, KnownCloudGitProvider.Unknown, gitKey, new Uri( $"https://{authority}/api/v3/" ) )
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
        return GitKey.GetWriteCredentials( monitor, out creds );
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
