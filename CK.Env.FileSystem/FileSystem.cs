using CK.Core;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Primitives;
using System.Collections;
using System.Linq;
using CK.Text;
using System.Diagnostics;
using LibGit2Sharp;

namespace CK.Env
{

    /// <summary>
    /// <see cref="IFileProvider"/> implementation that handles Git repositories.
    /// </summary>
    public class FileSystem : IFileProvider, IDisposable
    {
        readonly List<GitFolder> _gits;

        /// <summary>
        /// Initializes a new <see cref="FileSystem"/> on a physical root path.
        /// </summary>
        /// <param name="rootPath">Physical root path.</param>
        public FileSystem( string rootPath )
        {
            Root = new NormalizedPath( Path.GetFullPath( rootPath ) );
            _gits = new List<GitFolder>();
        }

        /// <summary>
        /// Automatic discovery of all git folders below the <see cref="Root"/>.
        /// </summary>
        public void DiscoverGitFolders( IWorldName world ) => DiscoverGitFolders( Root, world );

        void DiscoverGitFolders( string parent, IWorldName world )
        {
            foreach( var d in Directory.EnumerateDirectories( parent ) )
            {
                var gitFolder = Path.Combine( d, ".git" );
                if( Directory.Exists( gitFolder ) )
                {
                    _gits.Add( new GitFolder( this, gitFolder, LocalBlankFeedProvider, world ) );
                    return;
                }
                DiscoverGitFolders( d, world );
            }
        }

        /// <summary>
        /// Gets or sets the provider for blank feed local folder.
        /// </summary>
        public ILocalFeedProvider LocalBlankFeedProvider { get; set; }

        /// <summary>
        /// Gets the <see cref="GitFolder"/> loaded so far (<see cref="EnsureGitFolder(NormalizedPath)"/>).
        /// </summary>
        public IReadOnlyList<GitFolder> GitFolders => _gits;

        /// <summary>
        /// Gets the physical root path.
        /// </summary>
        public NormalizedPath Root { get; }

        /// <summary>
        /// Ensures that the Git folder is loaded.
        /// </summary>
        /// <param name="folderPath">
        /// The folder path is a sub path of <see cref="Root"/> and contains the .git sub folder.
        /// </param>
        /// <returns>The <see cref="GitFolder"/> or null if there is not .git subfolder.</returns>
        public GitFolder EnsureGitFolder( IActivityMonitor m, IWorldName world, NormalizedPath folderPath, string urlRepository = null )
        {
            GitFolder g = GitFolders.FirstOrDefault( f => f.SubPath == folderPath );
            if( g == null )
            {
                folderPath = Root.Combine( folderPath );
                var gitFolder = Path.Combine( folderPath, ".git" );
                if( !Directory.Exists( gitFolder ) )
                {
                    if( String.IsNullOrWhiteSpace( urlRepository ) )
                    {
                        m.Warn( "Url repository is not specified. Skipping automatic clone." );
                        return null;
                    }
                    using( m.OpenInfo( $"Checking out '{folderPath}' from '{urlRepository}' on {world.DevelopBranchName}." ) )
                    {
                        Repository.Clone( urlRepository, folderPath, new CloneOptions()
                        {
                            CredentialsProvider = GitFolder.DefaultCredentialHandler,
                            BranchName = world.DevelopBranchName,
                            Checkout = true
                        } );
                    }
                }
                g = new GitFolder( this, gitFolder, LocalBlankFeedProvider, world );
                _gits.Add( g );
            }
            return g;
        }

        /// <summary>
        /// Disposes this file system.
        /// </summary>
        public void Dispose()
        {
            foreach( var g in GitFolders )
            {
                g.Dispose();
            }
        }

        /// <summary>
        /// Tries to find the <see cref="GitFolder"/> for a path below the <see cref="Root"/>.
        /// </summary>
        /// <param name="subPath">The path (<see cref="Root"/> based). Can be any path inside the Git folder.</param>
        /// <returns>The Git folder or null if not found.</returns>
        public GitFolder FindGitFolder( NormalizedPath subPath ) => GitFolders.FirstOrDefault( f => subPath.StartsWith( f.SubPath, strict: false ) );

        /// <summary>
        /// Gets the directory content for a path below the <see cref="Root"/>.
        /// </summary>
        /// <param name="subpath">The subordinated path.</param>
        /// <returns>The directory content.</returns>
        public IDirectoryContents GetDirectoryContents( string subpath ) => GetDirectoryContents( new NormalizedPath( subpath ) );

        /// <summary>
        /// Gets the directory content for a path below the <see cref="Root"/>.
        /// </summary>
        /// <param name="subpath">The subordinated path.</param>
        /// <returns>The directory content.</returns>
        public IDirectoryContents GetDirectoryContents( NormalizedPath sub )
        {
            sub = sub.ResolveDots();
            GitFolder g = GitFolders.FirstOrDefault( f => sub.StartsWith( f.SubPath, strict: false ) );
            return g != null
                        ? g.GetDirectoryContents( sub.RemovePrefix( g.SubPath ) ) ?? NotFoundDirectoryContents.Singleton
                        : PhysicalGetDirectoryContents( sub );
        }

        /// <summary>
        /// Gets the file information for a path below the <see cref="Root"/>.
        /// </summary>
        /// <param name="subpath">The subordinated path.</param>
        /// <returns>The file info.</returns>
        public IFileInfo GetFileInfo( string subpath ) => GetFileInfo( new NormalizedPath( subpath ) );

        /// <summary>
        /// Gets the file information for a path below the <see cref="Root"/>.
        /// </summary>
        /// <param name="subpath">The subordinated path.</param>
        /// <returns>The file info.</returns>
        public IFileInfo GetFileInfo( NormalizedPath sub )
        {
            sub = sub.ResolveDots();
            GitFolder g = GitFolders.FirstOrDefault( f => sub.StartsWith( f.SubPath, strict: false ) );
            return g != null
                        ? g.GetFileInfo( sub.RemovePrefix( g.SubPath ) ) ?? new NotFoundFileInfo( sub.Path )
                        : PhysicalGetFileInfo( sub );
        }

        /// <summary>
        /// Copy a <see cref="IFileInfo"/> content to a <paramref name="destination"/> path in this
        /// file system.
        /// The destination must not be an existing folder and must be physically accessible
        /// (<see cref="IFileInfo.PhysicalPath"/> must not be null): if inside a <see cref="GitFolder"/>, it must
        /// be a in the current head (ie. corresponds to a file in the current working directory).
        /// </summary>
        /// <param name="m">The activity monitor.</param>
        /// <param name="source">The content source that must be an existing file (not a directory).</param>
        /// <param name="destination">The target path in this file system.</param>
        /// <returns>True on success, false on error.</returns>
        public bool CopyTo( IActivityMonitor m, IFileInfo source, NormalizedPath destination )
        {
            if( source == null || !source.Exists ) throw new ArgumentNullException( nameof( source ) );
            using( var content = source.CreateReadStream() )
            {
                return CopyTo( m, content, destination );
            }
        }

        /// <summary>
        /// Copy a text content to a <paramref name="destination"/> path in this
        /// file system.
        /// The destination must not be an existing folder and must be physically accessible
        /// (<see cref="IFileInfo.PhysicalPath"/> must not be null): if inside a <see cref="GitFolder"/>, it must
        /// be a in the current head (ie. corresponds to a file in the current working directory).
        /// </summary>
        /// <param name="m">The activity monitor.</param>
        /// <param name="content">The content text.</param>
        /// <param name="destination">The target path in this file system.</param>
        /// <returns>True on success, false on error.</returns>
        public bool CopyTo( IActivityMonitor m, string content, NormalizedPath destination )
        {
            if( content == null ) throw new ArgumentNullException( nameof( content ) );
            var fDest = GetWritableDestination( m, ref destination );
            if( fDest == null ) return false;
            using( m.OpenInfo( $"{(fDest.Exists ? "Replacing" : "Creating")} {destination}." ) )
                try
                {
                    File.WriteAllText( fDest.PhysicalPath, content );
                    return true;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return false;
                }
        }

        /// <summary>
        /// Copy a content to a <paramref name="destination"/> path in this
        /// file system.
        /// The destination must not be an existing folder and must be physically accessible
        /// (<see cref="IFileInfo.PhysicalPath"/> must not be null): if inside a <see cref="GitFolder"/>, it must
        /// be a in the current head (ie. corresponds to a file in the current working directory).
        /// </summary>
        /// <param name="m">The activity monitor.</param>
        /// <param name="content">The content source.</param>
        /// <param name="destination">The target path in this file system.</param>
        /// <returns>True on success, false on error.</returns>
        public bool CopyTo( IActivityMonitor m, Stream content, NormalizedPath destination )
        {
            IFileInfo fDest = GetWritableDestination( m, ref destination );
            if( fDest == null ) return false;
            using( m.OpenInfo( $"{(fDest.Exists ? "Replacing" : "Creating")} {destination}." ) )
                try
                {
                    using( var d = new FileStream( fDest.PhysicalPath, FileMode.Create, FileAccess.Write, FileShare.Read ) )
                    {
                        content.CopyTo( d );
                    }
                    return true;
                }
                catch( Exception ex )
                {
                    m.Fatal( ex );
                    return false;
                }
        }

        private IFileInfo GetWritableDestination( IActivityMonitor m, ref NormalizedPath destination )
        {
            destination = destination.ResolveDots();
            if( destination.IsEmpty ) throw new ArgumentNullException( nameof( destination ) );
            var fDest = GetFileInfo( destination );
            if( fDest.Exists && fDest.IsDirectory )
            {
                m.Error( $"Cannot replace a folder '{destination}' by a file content." );
                fDest = null;
            }
            if( fDest.Exists && fDest.PhysicalPath == null )
            {
                m.Error( $"Destination file '{destination}' is not writable." );
                fDest = null;
            }
            if( fDest != null )
            {
                string dir = Path.GetDirectoryName( fDest.PhysicalPath );
                if( !Directory.Exists( dir ) )
                {
                    m.Trace( $"Creating directory '{dir}'." );
                    Directory.CreateDirectory( dir );
                }
            }
            return fDest;
        }

        /// <summary>
        /// Deletes a file or folder. It must be physically accessible
        /// (<see cref="IFileInfo.PhysicalPath"/> must not be null): if inside a <see cref="GitFolder"/>, it must
        /// be a in the current head (ie. corresponds to a file in the current working directory).
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="subPath">The item path to delete.</param>
        /// <returns>True on success, false on error (error is logged into the monitor).</returns>
        public bool Delete( IActivityMonitor m, NormalizedPath subPath )
        {
            IFileInfo info = GetFileInfo( subPath );
            if( info.Exists )
            {
                if( info.PhysicalPath == null )
                {
                    m.Error( $"'{subPath}' to delete is not physically available." );
                    return false;
                }
                try
                {
                    if( info.IsDirectory )
                    {
                        m.Info( $"Deleting folder '{subPath}'." );
                        Directory.Delete( info.PhysicalPath, true );
                    }
                    else
                    {
                        m.Info( $"Deleting file '{subPath}'." );
                        File.Delete( info.PhysicalPath );
                    }
                }
                catch( Exception ex )
                {
                    m.Fatal( ex );
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Applies a diff to this file system.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="diff">The computed diff.</param>
        /// <returns>True on success, false on error (error is logged into the monitor).</returns>
        public bool ApplyDiff( IActivityMonitor m, FileProviderContentInfo.DiffResult diff )
        {
            if( diff.Origin.FileProvider != this ) throw new ArgumentException( "Originated from another file provider.", nameof( diff ) );
            if( diff.HasCaseConflicts ) throw new ArgumentException( "Case conflicts must be resolved first.", nameof( diff ) );
            foreach( var d in diff.Differences )
            {
                switch( d.Status )
                {
                    case FileProviderContentInfo.FileDiffStatus.ShouldCreate:
                    case FileProviderContentInfo.FileDiffStatus.ShouldUpdate:
                        if( !CopyTo( m, d.Other, diff.Origin.Root.Combine( d.Path ) ) ) return false;
                        break;
                    default:
                        Debug.Assert( d.Status == FileProviderContentInfo.FileDiffStatus.ShouldDelete );
                        if( !Delete( m, diff.Origin.Root.Combine( d.Path ) ) ) return false;
                        break;
                }
            }
            return true;
        }


        /// <summary>
        /// Helper that deletes a local directory (with linear timed retries).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="dirPath">The directory path on the local file system to delete.</param>
        public void RawDeleteLocalDirectory( IActivityMonitor m, string dirPath )
        {
            if( Directory.Exists( dirPath ) )
            {
                m.Info( $"Deleting {dirPath}." );
                int tryCount = 0;
                for(; ; )
                {
                    try
                    {
                        if( Directory.Exists( dirPath ) ) Directory.Delete( dirPath, true );
                        return;
                    }
                    catch( Exception ex )
                    {
                        m.Warn( $"Error while deleting {dirPath}.", ex );
                        if( ++tryCount > 4 ) throw;
                        System.Threading.Thread.Sleep( 100 * tryCount );
                    }
                }
            }
        }

        class FileSystemInfoWrapper : IFileInfo
        {
            readonly FileSystemInfo _info;

            public FileSystemInfoWrapper( FileSystemInfo f )
            {
                _info = f;
            }

            public bool Exists => true;

            public long Length => _info is FileInfo f ? f.Length : -1;

            public string PhysicalPath => _info.FullName;

            public string Name => _info.Name;

            public DateTimeOffset LastModified => _info.LastWriteTimeUtc;

            public bool IsDirectory => _info is DirectoryInfo;

            public Stream CreateReadStream()
            {
                // Same behavior as the standard PhysicalFileInfo.
                if( _info is FileInfo )
                {
                    // We are setting buffer size to 1 to prevent FileStream from allocating it's internal buffer
                    // 0 causes constructor to throw
                    var bufferSize = 1;
                    return new FileStream(
                        PhysicalPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan );
                }
                throw new InvalidOperationException( "Cannot create a stream for a directory." );
            }
        }

        class PhysicalDirectoryContents : IDirectoryContents
        {
            private readonly NormalizedPath _root;

            public PhysicalDirectoryContents( NormalizedPath root )
            {
                _root = root;
            }

            public bool Exists => true;

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                return new DirectoryInfo( _root )
                            .EnumerateFileSystemInfos()
                            .Select( i => new FileSystemInfoWrapper( i ) )
                            .GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
   
        internal IFileInfo PhysicalGetFileInfo( NormalizedPath sub )
        {
            Debug.Assert( sub == sub.ResolveDots() );
            var path = Root.Combine( sub );
            if( File.Exists( path ) ) return new FileSystemInfoWrapper( new FileInfo( path ) );
            if( Directory.Exists( path ) ) return new FileSystemInfoWrapper( new DirectoryInfo( path ) );
            return new PhysicalNotFoundFileInfo( path );
        }

        internal IDirectoryContents PhysicalGetDirectoryContents( NormalizedPath sub )
        {
            Debug.Assert( sub == sub.ResolveDots() );
           return new PhysicalDirectoryContents( Root.Combine( sub ) );
        }

        IChangeToken IFileProvider.Watch( string filter )
        {
            throw new NotImplementedException( "Sorry for that..." );
        }




    }
}
