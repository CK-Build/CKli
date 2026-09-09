using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Core.GitHosting.Providers;

sealed class FileSystemProvider : GitHostingProvider
{
    internal FileSystemProvider( IGitRepositoryAccessKey gitKey )
        : base( "file://", gitKey )
    {
    }

    public override bool CanArchiveRepository => false;

    /// <summary>
    /// A bare repository has a HEAD but no notion of a "default branch" exposed to its clients:
    /// <see cref="HasDefaultBranch"/> is false, this always throws.
    /// </summary>
    public override Task<bool> SetDefaultBranchAsync( IActivityMonitor monitor,
                                                      NormalizedPath repoPath,
                                                      string branchName,
                                                      CancellationToken cancellation = default )
    {
        Throw.CheckState( HasDefaultBranch );
        return Task.FromResult( false );
    }

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

    /// <inheritdoc />
    public override Task<(bool Success, byte[]? Content)> GetFileContentAsync( IActivityMonitor monitor,
                                                                               NormalizedPath repoPath,
                                                                               NormalizedPath filePath,
                                                                               string? refName = null,
                                                                               LogLevel notFoundLogLevel = LogLevel.Trace,
                                                                               CancellationToken cancellation = default )
    {
        Throw.CheckArgument( !filePath.IsEmptyPath );
        return Task.FromResult( GetFileContent( monitor, repoPath, filePath, refName, notFoundLogLevel ) );
    }

    (bool Success, byte[]? Content) GetFileContent( IActivityMonitor monitor,
                                                    NormalizedPath repoPath,
                                                    NormalizedPath filePath,
                                                    string? refName,
                                                    LogLevel notFoundLogLevel )
    {
        using var _ = monitor.OpenInfo( $"Reading file '{filePath}' of '{repoPath}'@'{refName ?? "(HEAD)"}'." );
        try
        {
            var pGit = Path.Combine( Path.GetFullPath( repoPath ), ".git" );
            if( !Directory.Exists( pGit ) )
            {
                monitor.Log( notFoundLogLevel, $"Repository '{BaseUrl}{repoPath}' not found: no file to read." );
                return (true, null);
            }
            using var repo = new Repository( pGit );
            // HasDefaultBranch is false here: a bare repository exposes no default branch to its clients,
            // its HEAD is the closest equivalent and it is what a clone would land on.
            Commit? commit;
            if( refName == null )
            {
                commit = repo.Head.Tip;
                if( commit == null )
                {
                    monitor.Log( notFoundLogLevel, $"Repository '{BaseUrl}{repoPath}' HEAD has no commit." );
                    return (true, null);
                }
            }
            else
            {
                // A branch, then a tag, then anything the object database can resolve (a sha, "HEAD~2"...).
                commit = repo.Branches[refName]?.Tip
                         ?? (repo.Tags[refName]?.PeeledTarget as Commit)
                         ?? repo.Lookup<Commit>( refName );
                if( commit == null )
                {
                    monitor.Log( notFoundLogLevel, $"No branch, tag or commit '{refName}' in '{BaseUrl}{repoPath}'." );
                    return (true, null);
                }
            }
            var entry = commit[filePath];
            if( entry == null )
            {
                monitor.Log( notFoundLogLevel, $"File '{filePath}' not found in '{BaseUrl}{repoPath}'@{commit.Sha}." );
                return (true, null);
            }
            if( entry.TargetType != TreeEntryTargetType.Blob )
            {
                monitor.Error( $"'{filePath}' in '{BaseUrl}{repoPath}'@{commit.Sha} is a {entry.TargetType}, not a file." );
                return (false, null);
            }
            var blob = (Blob)entry.Target;
            using var s = blob.GetContentStream();
            var content = new byte[blob.Size];
            s.ReadExactly( content );
            monitor.Trace( $"Read {content.Length} byte(s)." );
            return (true, content);
        }
        catch( Exception ex )
        {
            monitor.Error( $"While reading file '{filePath}' of '{repoPath}'.", ex );
            return (false, null);
        }
    }

    public override Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                       NormalizedPath repoPath,
                                                                       bool? isPrivate,
                                                                       string defaultBranchName,
                                                                       CancellationToken cancellation = default )
    {
        return Task.FromResult( CreateRepository( monitor, repoPath, defaultBranchName ) );
    }

    HostedRepositoryInfo? CreateRepository( IActivityMonitor monitor, NormalizedPath repoPath, string defaultBranchName )
    {
        if( repoPath.Parts.Any( s => StringComparer.OrdinalIgnoreCase.Equals( s, ".git" ) ) )
        {
            monitor.Error( $"Cannot create a repository inside another one at '{repoPath}'." );
            return null;
        }
        if( Directory.Exists( repoPath ) )
        {
            monitor.Error( $"Directory already exists at '{repoPath}'." );
            return null;
        }
        Signature? committer = null; 
        using Repository? r = GitRepository.InitOrphanOrBareRepository( monitor, repoPath, defaultBranchName, ref committer, isBare: true );
        if( r == null )
        {
            return null;
        }
        var createdAt = Directory.GetCreationTimeUtc( r.Info.Path );
        return new HostedRepositoryInfo
        {
            RepoPath = repoPath,
            CloneUrl = "file://" + repoPath,
            CreatedAt = createdAt,
            IsPrivate = !IsDefaultPublic,
            UpdatedAt = createdAt,
        };
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
            using var repo = new Repository( Path.Combine( repoPath, ".git" ) );
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

    public override Task<List<PublishedReleaseInfo>?> GetReleaseListAsync( IActivityMonitor monitor,
                                                                        NormalizedPath repoPath,
                                                                        int pageNumber,
                                                                        int countPerPage,
                                                                        CancellationToken cancellation = default )
    {
        try
        {
            if( !CheckReleasePaths( monitor, repoPath, null, releaseMustExist: false ) )
            {
                return Task.FromResult<List<PublishedReleaseInfo>?>( null );
            }
            var result = new List<PublishedReleaseInfo>();
            var releases = repoPath.AppendPart( "Releases" );
            if( Directory.Exists( releases ) )
            {
                var dirs = Directory.GetDirectories( releases );
                // Order by name descending (recent tags first) — providers may implement other ordering.
                Array.Sort( dirs );
                Array.Reverse( dirs );
                // Normalize paging parameters
                if( pageNumber < 1 ) pageNumber = 1;
                if( countPerPage <= 0 ) countPerPage = dirs.Length;
                int skip = (pageNumber - 1) * countPerPage;
                for( int i = skip; i < Math.Min( dirs.Length, skip + countPerPage ); i++ )
                {
                    NormalizedPath releaseDir = dirs[i];
                    var name = releaseDir.LastPart;
                    Throw.DebugAssert( name != null );
                    result.Add( new PublishedReleaseInfo
                    {
                        Version = SVersion.Parse( name ),
                        ReleaseId = releaseDir,
                        CreatedAt = Directory.GetCreationTimeUtc( releaseDir ),
                        IsPublished = true,
                        Description = "",
                        Assets = Directory.GetFiles( releaseDir ).Select( p => p.Substring( releaseDir.Path.Length + 1 ) ).ToList(),
                    } );
                }
            }
            return Task.FromResult<List<PublishedReleaseInfo>?>( result );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While listing releases for '{repoPath}'.", ex );
            return Task.FromResult<List<PublishedReleaseInfo>?>( null );
        }
    }


    public override Task<string?> CreateDraftReleaseAsync( IActivityMonitor monitor, NormalizedPath repoPath, string versionedTag, CancellationToken cancellation = default )
    {
        try
        {
            if( !CheckReleasePaths( monitor, repoPath, null, releaseMustExist: false ) )
            {
                return Task.FromResult<string?>( null );
            }
            var releases = repoPath.AppendPart( "Releases" );
            Directory.CreateDirectory( releases );
            var releaseFolder = releases.AppendPart( versionedTag );
            if( Directory.Exists( releaseFolder) )
            {
                monitor.Error( $"Release '{versionedTag}' already exists at '{repoPath}'." );
                return Task.FromResult<string?>( null );
            }
            Directory.CreateDirectory( releaseFolder );
            return Task.FromResult<string?>( releaseFolder.Path );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While creating draft in '{repoPath}' for '{versionedTag}'.", ex );
            return Task.FromResult<string?>( null );
        }
    }

    public override Task<bool> AddReleaseAssetAsync( IActivityMonitor monitor,
                                                     NormalizedPath repoPath,
                                                     string releaseId,
                                                     NormalizedPath filePath,
                                                     string? fileName = null,
                                                     CancellationToken cancellation = default )
    {
        if( !CheckReleasePaths( monitor, repoPath, releaseId, releaseMustExist: true ) )
        {
            return Task.FromResult( false );
        }
        try
        {
            fileName ??= filePath.LastPart;
            var target = Path.Combine( releaseId, fileName );
            File.Copy( filePath, target );
            return Task.FromResult( true );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While adding an asset to the draft release in '{releaseId}'.", ex );
            return Task.FromResult( false );
        }
    }

    public override Task<(bool Success, PublishedReleaseInfo? Info)> GetReleaseAsync( IActivityMonitor monitor,
                                                                                      NormalizedPath repoPath,
                                                                                      string releaseId,
                                                                                      LogLevel notFoundLogLevel = LogLevel.Trace,
                                                                                      CancellationToken cancellation = default )
    {
        try
        {
            if( !CheckReleasePaths( monitor, repoPath, releaseId, releaseMustExist: false ) )
            {
                return Task.FromResult( (false, (PublishedReleaseInfo?)null) );
            }
            if( !Directory.Exists( releaseId ) )
            {
                return Task.FromResult( LogReleaseNotFound( monitor, notFoundLogLevel, repoPath, releaseId ) );
            }
            var name = Path.GetFileName( releaseId );
            var assets = Directory.GetFiles( releaseId ).Select( p => Path.GetFileName( p ) ?? p ).ToList();
            var info = new PublishedReleaseInfo
            {
                Version = SVersion.Parse( name ),
                ReleaseId = releaseId,
                CreatedAt = Directory.GetCreationTimeUtc( releaseId ),
                IsPublished = false,
                Description = "",
                Assets = assets,
            };
            return Task.FromResult( (true, info) )!;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While getting release '{releaseId}'.", ex );
            return Task.FromResult( (false, (PublishedReleaseInfo?)null) );
        }
    }

    public override Task<bool> DeleteReleaseAsync( IActivityMonitor monitor,
                                                   NormalizedPath repoPath,
                                                   string releaseId,
                                                   CancellationToken cancellation = default )
    {
        try
        {
            if( !CheckReleasePaths( monitor, repoPath, releaseId, releaseMustExist: false ) )
            {
                return Task.FromResult( false );
            }
            if( !Directory.Exists( releaseId ) )
            {
                // Idempotent: deleting a non-existing release is a no-op.
                monitor.Trace( $"Delete succeeds: release doesn't exist at '{releaseId}'." );
                return Task.FromResult( true );
            }

            return Task.FromResult( FileHelper.DeleteFolder( monitor, releaseId ) );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While deleting release '{releaseId}'.", ex );
            return Task.FromResult( false );
        }
    }


    static bool CheckReleasePaths( IActivityMonitor monitor,
                                   NormalizedPath repoPath,
                                   string? releaseId,
                                   bool releaseMustExist )
    {
        if( !Directory.Exists( repoPath ) )
        {
            monitor.Error( $"Invalid File System repository path '{repoPath}'." );
            return false;
        }
        if( releaseId != null )
        {
            var releasesDir = repoPath.AppendPart( "Releases" );
            if( releaseId.Length <= releasesDir.Path.Length + 2
                || releaseId[releasesDir.Path.Length] != NormalizedPath.DirectorySeparatorChar
                || !releaseId.StartsWith( releasesDir, StringComparison.OrdinalIgnoreCase ) )
            {
                monitor.Error( $"""
                                Release identifier must be inside the repository Releases folder:'.
                                Releases folder: '{releasesDir}'.
                                Release identifier: '{releaseId}'.
                                """ );
                return false;
            }
            if( releaseMustExist && !Directory.Exists( releaseId ) )
            {
                monitor.Error( $"Release identifier '{releaseId}' doesn't exist." );
                return false;
            }
        }
        return true;
    }
}
