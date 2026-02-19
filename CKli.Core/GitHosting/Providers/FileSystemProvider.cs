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
        : base( "file://", gitKey )
    {
    }

    public override bool CanArchiveRepository => false;

    protected internal override NormalizedPath GetRepositoryPathFromUrl( IActivityMonitor monitor, GitRepositoryKey key )
    {
        Throw.DebugAssert( key.OriginUrl.Scheme == Uri.UriSchemeFile );
        return key.OriginUrl.LocalPath;
    }

    public override Task<HostedRepositoryInfo?> GetRepositoryInfoAsync( IActivityMonitor monitor,
                                                                        NormalizedPath repoPath,
                                                                        bool mustExist,
                                                                        CancellationToken cancellation = default )
    {
        return Task.FromResult<HostedRepositoryInfo?>( GetRepositoryInfo( monitor, repoPath, mustExist ) );
    }


    HostedRepositoryInfo? GetRepositoryInfo( IActivityMonitor monitor, NormalizedPath repoPath, bool mustExist )
    {
        try
        {
            var pGit = Path.Combine( Path.GetFullPath( repoPath ), ".git" );
            if( !Directory.Exists( pGit ) )
            {
                if( mustExist )
                {
                    monitor.Error( $"Expected Git repository at '{BaseUrl}{repoPath}' is missing." );
                    return null;
                }
                return new HostedRepositoryInfo { RepoPath = default };
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
                IsPrivate = !IsDefaultPublic,
                UpdatedAt = Directory.GetLastWriteTimeUtc( pGit ),
            };
        }
        catch( Exception ex )
        {
            monitor.Error( $"While getting repository info for '{repoPath}'.", ex );
            return null;
        }
    }

    public override Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                       NormalizedPath repoPath,
                                                                       bool? isPrivate = null,
                                                                       CancellationToken cancellation = default )
    {
        return Task.FromResult<HostedRepositoryInfo?>( CreateRepository( monitor, repoPath ) );
    }

    HostedRepositoryInfo? CreateRepository( IActivityMonitor monitor, NormalizedPath repoPath )
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
            Throw.DebugAssert( "The repo is empty. The head is 'unborn'.", repo.Head.Tip == null );
            return new HostedRepositoryInfo
            {
                RepoPath = repoPath,
                CloneUrl = "file://" + repoPath,
                CreatedAt = Directory.GetCreationTimeUtc( pGit ),
                IsPrivate = !IsDefaultPublic,
                UpdatedAt = Directory.GetLastWriteTimeUtc( pGit ),
            };
        }
        catch( Exception ex )
        {
            monitor.Error( $"While creating repository '{repoPath}'.", ex );
            return null;
        }
    }

    public override Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor, NormalizedPath repoPath, CancellationToken cancellation = default )
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

    public override Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor,
                                                       NormalizedPath repoPath,
                                                       bool archive,
                                                       CancellationToken cancellation = default )
    {
        return Task.FromException<bool>( new NotSupportedException( ProviderType ) );
    }
}
