using CK.Core;
using CK.SimpleKeyVault;
using CK.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// <see cref="IFileProvider"/> implementation that handles Git repositories.
    /// Exposes its <see cref="ServiceContainer"/> that is the root of services
    /// available to <see cref="IGitPlugin"/> and <see cref="IGitBranchPlugin"/>.
    /// </summary>
    public class FileSystem : IFileProvider, IDisposable
    {
        readonly CommandRegister _commandRegister;
        readonly SecretKeyStore _secretKeyStore;
        readonly List<ProtoGitFolder> _protoGits;
        readonly List<GitRepository> _gits;

        /// <summary>
        /// Initializes a new <see cref="FileSystem"/> on a physical root path.
        /// </summary>
        /// <param name="rootPath">Physical root path.</param>
        /// <param name="commandRegister">Command register.</param>
        /// <param name="sp">Optional base services.</param>
        public FileSystem(
            string rootPath,
            CommandRegister commandRegister,
            SecretKeyStore secretKeyStore,
            IServiceProvider sp )
        {
            Root = new NormalizedPath( Path.GetFullPath( rootPath ) );
            _commandRegister = commandRegister;
            _secretKeyStore = secretKeyStore;
            _protoGits = new List<ProtoGitFolder>();
            _gits = new List<GitRepository>();
            ServiceContainer = new SimpleServiceContainer( sp );
            ServiceContainer.Add( this );
            ServiceContainer.Add<IFileProvider>( this );
        }

        /// <summary>
        /// Gets the service container at the file system level.
        /// Each <see cref="GitRepository.ServiceContainer"/> are bound to this one.
        /// </summary>
        public SimpleServiceContainer ServiceContainer { get; }

        /// <summary>
        /// Gets the <see cref="GitRepository"/> loaded so far (see <see cref="EnsureGitFolder(NormalizedPath)"/>).
        /// </summary>
        public IReadOnlyList<GitRepository> GitFolders => _gits;

        /// <summary>
        /// Gets or sets whether this file system is the only one that interacts with the actual files.
        /// When true, some operations can be done more safely (or efficienty).
        /// Defaults to false.
        /// </summary>
        public bool ServerMode { get; set; }

        /// <summary>
        /// Gets the physical root path.
        /// </summary>
        public NormalizedPath Root { get; }

        /// <summary>
        /// Finds or creates a <see cref="ProtoGitFolder"/>.
        /// </summary>
        /// <param name="folderPath">The folder relative to the <see cref="Root"/> and contains the .git sub folder.</param>
        /// <param name="urlRepository">The url repository.</param>
        /// <param name="isPublic">Whether the repository is public.</param>
        /// <returns>The <see cref="ProtoGitFolder"/>.</returns>
        public ProtoGitFolder FindOrCreateProtoGitFolder( IActivityMonitor m, IWorldName world, NormalizedPath folderPath, string urlRepository, bool isPublic = false )
        {
            ProtoGitFolder g = _protoGits.FirstOrDefault( f => f.FolderPath == folderPath );
            if( g == null )
            {
                g = new ProtoGitFolder( new Uri( urlRepository ), isPublic, folderPath, world, _secretKeyStore, this, _commandRegister );
                _protoGits.Add( g );
            }
            return g;
        }

        /// <summary>
        /// Ensures that the Git folder is loaded.
        /// The <see cref="ProtoGitFolder"/> must have been created first.
        /// </summary>
        /// <param name="folderPath">
        /// The folder path is a sub path of <see cref="Root"/> and contains the .git sub folder.
        /// </param>
        /// <returns>The <see cref="GitRepository"/> or null on error.</returns>
        public GitRepository EnsureGitFolder( IActivityMonitor m, ProtoGitFolder proto )
        {
            if( proto == null ) throw new ArgumentNullException( nameof( proto ) );
            if( proto.FileSystem != this ) throw new ArgumentException( "FileSystem mismatch.", nameof( proto ) );
            GitRepository g = GitFolders.FirstOrDefault( f => f.ProtoGitFolder == proto );
            if( g == null )
            {
                g = proto.CreateGitFolder( m );
                // TODO: this SHOULD be done in ProtoGitFolder.EnsureWorkingFolder!
                if( g != null )
                {
                    _gits.Add( g );
                }
            }
            return g;
        }

        /// <summary>
        /// Ensures that the Git folder is loaded.
        /// The <see cref="ProtoGitFolder"/> must have been created first.
        /// </summary>
        /// <param name="folderPath">
        /// The folder path is a sub path of <see cref="Root"/> and contains the .git sub folder.
        /// </param>
        /// <returns>The <see cref="GitRepository"/> or null if not found.</returns>
        public GitRepository EnsureGitFolder( IActivityMonitor m, NormalizedPath folderPath )
        {
            ProtoGitFolder pg = _protoGits.FirstOrDefault( f => f.FolderPath == folderPath );
            return pg != null ? EnsureGitFolder( m, pg ) : null;
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
        /// Tries to find the <see cref="GitRepository"/> for a path below the <see cref="Root"/>.
        /// </summary>
        /// <param name="subPath">The path (<see cref="Root"/> based). Can be any path inside the Git folder.</param>
        /// <returns>The Git folder or null if not found.</returns>
        public GitRepository FindGitFolder( NormalizedPath subPath ) => GitFolders.FirstOrDefault( f => subPath.StartsWith( f.SubPath, strict: false ) );

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
            sub = sub.ResolveDots().With( NormalizedPathRootKind.None );
            GitRepository g = GitFolders.FirstOrDefault( f => sub.StartsWith( f.SubPath, strict: false ) );
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
        /// Never return null: <see cref="NotFoundFileInfo"/> is returned when the file does not exist.
        /// </summary>
        /// <param name="subpath">The subordinated path.</param>
        /// <returns>The file info.</returns>
        public IFileInfo GetFileInfo( NormalizedPath sub )
        {
            sub = sub.ResolveDots().With( NormalizedPathRootKind.None );
            GitRepository g = GitFolders.FirstOrDefault( f => sub.StartsWith( f.SubPath, strict: false ) );
            return g != null
                        ? g.GetFileInfo( sub.RemovePrefix( g.SubPath ) ) ?? new NotFoundFileInfo( sub.Path )
                        : PhysicalGetFileInfo( sub );
        }

        /// <summary>
        /// Copy a <see cref="IFileInfo"/> content to a <paramref name="destination"/> path in this
        /// file system.
        /// The destination must not be an existing folder and must be physically accessible
        /// (<see cref="IFileInfo.PhysicalPath"/> must not be null): if inside a <see cref="GitRepository"/>, it must
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
        /// (<see cref="IFileInfo.PhysicalPath"/> must not be null): if inside a <see cref="GitRepository"/>, it must
        /// be a in the current head (ie. corresponds to a file in the current working directory).
        /// </summary>
        /// <param name="m">The activity monitor.</param>
        /// <param name="content">The content text.</param>
        /// <param name="destination">The target path in this file system.</param>
        /// <returns>True on success, false on error.</returns>
        public bool CopyTo( IActivityMonitor m, string content, NormalizedPath destination, Encoding encoding = null )
        {
            if( content == null ) throw new ArgumentNullException( nameof( content ) );
            var fDest = GetWritableDestination( m, ref destination );
            if( fDest == null ) return false;
            using( m.OpenInfo( $"{(fDest.Exists ? "Replacing" : "Creating")} {destination}." ) )
            {
                try
                {
                    if( encoding == null )
                    {
                        File.WriteAllText( fDest.PhysicalPath, content );
                    }
                    else
                    {
                        File.WriteAllText( fDest.PhysicalPath, content, encoding );
                    }
                    return true;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return false;
                }
            }
        }

        /// <summary>
        /// Copy a content to a <paramref name="destination"/> path in this
        /// file system.
        /// The destination must not be an existing folder and must be physically accessible
        /// (<see cref="IFileInfo.PhysicalPath"/> must not be null): if inside a <see cref="GitRepository"/>, it must
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
            {
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
        }

        /// <summary>
        /// Ensures that a path is a directory, creating it as necessary (if
        /// the path is writable).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="dir">The target path in this file system.</param>
        /// <returns>True on success, false on error.</returns>
        public bool EnsureDirectory( IActivityMonitor m, NormalizedPath dir )
        {
            dir = dir.ResolveDots();
            if( dir.IsEmptyPath ) throw new ArgumentNullException( nameof( dir ) );
            var p = GetFileInfo( dir );
            if( !p.Exists )
            {
                if( p.PhysicalPath == null )
                {
                    m.Error( $"Directory path '{dir}' is not writable." );
                    return false;
                }
                if( !Directory.Exists( p.PhysicalPath ) )
                {
                    m.Trace( $"Creating directory '{p.PhysicalPath}'." );
                    Directory.CreateDirectory( p.PhysicalPath );
                }
            }
            else if( !p.IsDirectory )
            {
                m.Error( $"Path '{dir}' is a file. Cannot transform it into a directory." );
                return false;
            }
            return true;
        }

        IFileInfo GetWritableDestination( IActivityMonitor m, ref NormalizedPath destination )
        {
            destination = destination.ResolveDots();
            if( destination.IsEmptyPath ) throw new ArgumentNullException( nameof( destination ) );
            var fDest = GetFileInfo( destination );
            if( fDest.Exists && fDest.IsDirectory )
            {
                m.Error( $"Cannot replace a folder '{destination}' by a file content." );
                fDest = null;
            }
            if( fDest.PhysicalPath == null )
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
        /// (<see cref="IFileInfo.PhysicalPath"/> must not be null): if inside a <see cref="GitRepository"/>, it must
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
                        FileHelper.RawDeleteLocalDirectory( m, info.PhysicalPath );
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
