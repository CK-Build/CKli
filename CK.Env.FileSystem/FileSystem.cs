using CK.Core;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Primitives;
using System.Collections;
using System.Linq;

namespace CK.Env
{
    public class FileSystem : IFileProvider, IDisposable
    {
        readonly PhysicalFileProvider _root;
        readonly List<GitFolder> _gits;

        public FileSystem( string rootPath )
        {
            Root = new NormalizedPath( Path.GetFullPath( rootPath ) );
            _root = new PhysicalFileProvider( Root.Path );
            _gits = new List<GitFolder>();
            DiscoverGitFolders( Root.Path );
        }

        public IReadOnlyList<GitFolder> GitFolders => _gits;

        public NormalizedPath Root { get; }

        public void Dispose()
        {
            _root.Dispose();
            foreach( var g in GitFolders )
            {
                g.Dispose();
            }
        }

        public IDirectoryContents GetDirectoryContents( string subpath ) => GetDirectoryContents( new NormalizedPath( subpath ) );

        public IDirectoryContents GetDirectoryContents( NormalizedPath sub )
        {
            GitFolder g = GitFolders.FirstOrDefault( f => sub.StartsWith( f.SubPath, strict: false ) );
            return g != null
                        ? g.GetDirectoryContents( sub.RemovePrefix( g.SubPath ) ) ?? NotFoundDirectoryContents.Singleton
                        : DoGetDirectoryContents( sub );
        }

        public IFileInfo GetFileInfo( string subpath ) => GetFileInfo( new NormalizedPath( subpath ) );

        public IFileInfo GetFileInfo( NormalizedPath sub )
        {
            GitFolder g = GitFolders.FirstOrDefault( f => sub.StartsWith( f.SubPath, strict: false ) );
            return g != null
                        ? g.GetFileInfo( sub.RemovePrefix( g.SubPath ) ) ?? new NotFoundFileInfo( sub.Path )
                        : DoGetFileInfo( sub );
        }

        public bool CopyTo( IActivityMonitor m, IDirectoryContents source, NormalizedPath destination )
        {
            if( source == null || !source.Exists ) throw new ArgumentNullException( nameof( source ) );
            if( destination.IsEmpty ) throw new ArgumentNullException( nameof( destination ) );
            var fDest = GetFileInfo( destination );
            if( fDest.Exists && !fDest.IsDirectory )
            {
                m.Error( $"Cannot copy a folder into '{destination}' since it is a file." );
                return false;
            }
            if( fDest.PhysicalPath == null )
            {
                m.Error( $"Destination folder '{destination}' is not writable." );
                return false;
            }
            m.Fatal( $"Folder copy is not implemented." );
            return false;
        }

        public bool CopyTo( IActivityMonitor m, IFileInfo source, NormalizedPath destination )
        {
            if( source == null || !source.Exists ) throw new ArgumentNullException( nameof( source ) );
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
                    using( var d = File.OpenWrite( fDest.PhysicalPath ) )
                    {
                        s.CopyTo( d );
                    }
                    return true;
                }
                catch( Exception ex )
                {
                    m.Fatal( ex );
                    return false;
                }
        }

        internal IFileInfo DoGetFileInfo( NormalizedPath sub ) => _root.GetFileInfo( sub.Path );

        internal IDirectoryContents DoGetDirectoryContents( NormalizedPath sub ) => _root.GetDirectoryContents( sub.Path );

        IChangeToken IFileProvider.Watch( string filter )
        {
            throw new NotImplementedException();
        }

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
}
