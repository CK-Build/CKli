using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CK.Core;
using LibGit2Sharp;

namespace CKli.Core.GitHosting.Providers;

sealed class FileSystemProvider : GitHostingProvider
{
    internal FileSystemProvider( IGitRepositoryAccessKey gitKey )
        : base( "file://", KnownCloudGitProvider.FileSystem, gitKey )
    {
    }

    public override bool CanArchiveRepository => false;

    public override Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor,
                                                       NormalizedPath repoPath,
                                                       CancellationToken ct = default )
    {
        return Task.FromException<bool>( new NotSupportedException( ProviderType ) );
    }

    public override Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                       NormalizedPath repoPath,
                                                                       HostedRepositoryCreateOptions? options = null,
                                                                       CancellationToken ct = default )
    {
        return Task.FromResult<HostedRepositoryInfo?>( CreateRepository( monitor, repoPath, options ) );
    }

    HostedRepositoryInfo? CreateRepository( IActivityMonitor monitor, NormalizedPath repoPath, HostedRepositoryCreateOptions? options )
    {
        try
        {
            if( repoPath.Parts.Any( s => StringComparer.OrdinalIgnoreCase.Equals( s, ".git" ) ) )
            {
                monitor.Error( $"Cannot create a repository inside another one at '{repoPath}'." );
                return null;
            }
            var folder = Path.GetFullPath( repoPath );
            if( Directory.Exists( folder ) )
            {
                monitor.Error( $"Directory already exists at '{repoPath}'." );
                return null;
            }
            var pGit = Path.Combine( folder, ".git" );
            using var repo = new Repository( Repository.Init( pGit, isBare: true ) );
            Throw.DebugAssert( repo.Head.Tip == null );
            var defBranchName = repo.Head.FriendlyName;
            Throw.DebugAssert( "libgit2 defaults to master.", defBranchName == "master" );
            // But... I failed to change the default branch. The rewriting of the HEAD file works
            // but the Repository.Clone sticks to master (unlike git clone). So I suspect libgit2
            // here to be a little bit lost when the repository is empty. This has to be investigated
            // but I spent way too much time on this, so give upe for now.
            bool isEmpty = true;
            if( options != null )
            {
                bool notSupported = options.AutoInit
                                    || options.DefaultBranch != null
                                    || options.LicenseTemplate != null
                                    || options.Description != null
                                    || options.GitIgnoreTemplate != null;
                if( notSupported )
                {
                    monitor.Warn( "Repository creation option DefaultBranch, AutoInit, LicenseTemplate, Description and GitIgnoreTemplate " +
                                  "are ignored by the FileSystemProvider." );
                }
            }
            return new HostedRepositoryInfo
            {
                RepoPath = repoPath,
                CloneUrl = "file://" + repoPath,
                CreatedAt = Directory.GetCreationTimeUtc( pGit ),
                DefaultBranch = "master",
                IsPrivate = options?.IsPrivate ?? !IsDefaultPublic,
                IsEmpty = isEmpty,
                UpdatedAt = Directory.GetLastWriteTimeUtc( pGit ),
            };
        }
        catch( Exception ex )
        {
            monitor.Error( $"While creating repository '{repoPath}'.", ex );
            return null;
        }
    }

    public override Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor, NormalizedPath repoPath, CancellationToken ct = default )
    {
        try
        {
            if( !Directory.Exists( repoPath ) )
            {
                monitor.Trace( $"Delete succeeds: repository doesn't exist at '{repoPath}'." );
                return Task.FromResult( true );
            }
            if( repoPath.Parts.Any( s => StringComparer.OrdinalIgnoreCase.Equals( s, ".git" ) ) )
            {
                monitor.Error( $"Cannot delete a repository inside another one at '{repoPath}'." );
                return Task.FromResult( false );
            }
            using var repo = new Repository( repoPath );
            if( !repo.Info.IsBare )
            {
                monitor.Error( $"Cannot delete a non bare repository at {repoPath}." );
                return Task.FromResult( false );
            }
            return Task.FromResult( FileHelper.DeleteFolder( monitor, repoPath ) );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While deleting repository info for '{repoPath}'.", ex );
            return Task.FromResult( false );
        }
    }

    public override Task<HostedRepositoryInfo?> GetRepositoryInfoAsync( IActivityMonitor monitor,
                                                                        NormalizedPath repoPath,
                                                                        CancellationToken ct = default )
    {
        return Task.FromResult<HostedRepositoryInfo?>( GetRepositoryInfo( monitor, repoPath ) );
    }

    HostedRepositoryInfo? GetRepositoryInfo( IActivityMonitor monitor, NormalizedPath repoPath )
    {
        try
        {
            var pGit = Path.Combine( Path.GetFullPath( repoPath ), ".git" );
            if( !Directory.Exists( pGit ) )
            {
                monitor.Error( $"Directory .git not found at '{repoPath}'." );
                return null;
            }
            using var repo = new Repository( pGit );
            if( !repo.Info.IsBare )
            {
                monitor.Error( $"Expected bare .git repository at '{repoPath}'." );
                return null;
            }
            return new HostedRepositoryInfo
            {
                RepoPath = repoPath,
                CloneUrl = "file://" + repoPath,
                CreatedAt = Directory.GetCreationTimeUtc( pGit ),
                DefaultBranch = repo.Head?.FriendlyName,
                IsPrivate = !IsDefaultPublic,
                IsEmpty = !repo.Commits.Any(),
                UpdatedAt = Directory.GetLastWriteTimeUtc( pGit ),
            };
        }
        catch( Exception ex )
        {
            monitor.Error( $"While getting repository info for '{repoPath}'.", ex );
            return null;
        }
    }
}
