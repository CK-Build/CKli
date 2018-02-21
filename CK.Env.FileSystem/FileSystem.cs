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
        /// <param name="autoDiscoverAllGitFolders">
        /// True to discover all Git folders below: calling <see cref="EnsureGitFolder(NormalizedPath)"/> is no
        /// more required but this can be a lengthy operation if there is a lot og Git folders to discover.
        /// </param>
        public FileSystem( string rootPath, bool autoDiscoverAllGitFolders = false )
        {
            Root = new NormalizedPath( Path.GetFullPath( rootPath ) );
            _gits = new List<GitFolder>();
            if( autoDiscoverAllGitFolders ) DiscoverGitFolders( Root );

            void DiscoverGitFolders( string parent )
            {
                foreach( var d in Directory.EnumerateDirectories( parent ) )
                {
                    var gitFolder = Path.Combine( d, ".git" );
                    if( Directory.Exists( gitFolder ) )
                    {
                        _gits.Add( new GitFolder( this, gitFolder ) );
                        return;
                    }
                    DiscoverGitFolders( d );
                }
            }
        }

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
        public GitFolder EnsureGitFolder( NormalizedPath folderPath )
        {
            GitFolder g = GitFolders.FirstOrDefault( f => f.SubPath == folderPath );
            if( g == null )
            {
                folderPath = Root.Combine( folderPath );
                var gitFolder = Path.Combine( folderPath, ".git" );
                if( Directory.Exists( gitFolder ) )
                {
                    g = new GitFolder( this, gitFolder );
                    _gits.Add( g );
                }
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
            destination = destination.ResolveDots();
            if( destination.IsEmpty ) throw new ArgumentNullException( nameof( destination ) );
            var fDest = GetFileInfo( destination );
            if( fDest.Exists && fDest.IsDirectory )
            {
                m.Error( $"Cannot replace a file '{destination}' by a folder." );
                return false;
            }
            if( fDest.PhysicalPath == null )
            {
                m.Error( $"Destination file '{destination}' is not writable." );
                return false;
            }
            using( m.OpenInfo( $"Replacing {destination}." ) )
                try
                {
                    using( var s = source.CreateReadStream() )
                    {
                        string dir = Path.GetDirectoryName( fDest.PhysicalPath );
                        if( !Directory.Exists( dir ) )
                        {
                            m.Trace( $"Creating directory '{dir}'." );
                            Directory.CreateDirectory( dir );
                        }
                        using( var d = new FileStream( fDest.PhysicalPath, FileMode.Create, FileAccess.Write, FileShare.Read ) )
                        {
                            s.CopyTo( d );
                        }
                    }
                    return true;
                }
                catch( Exception ex )
                {
                    m.Fatal( ex );
                    return false;
                }
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
                        Directory.Delete( info.PhysicalPath, true );
                    }
                    else
                    {
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
            return new NotFoundFileInfo( path );
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
