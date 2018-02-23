using CK.Env;
using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.FileProviders;
using CK.Text;

namespace CKli
{
    public class XReferentialFolder : XRunnable
    {
        readonly string _path;
        readonly FileSystem _fs;

        public XReferentialFolder(
            Initializer initializer,
            FileSystem fs)
            : base(initializer)
        {
            _fs = fs;
            _path = (string)initializer.Element.AttributeRequired( "Path" );
            FileProvider = new FileSystem( Path.Combine( fs.Root.Path, _path) );
            initializer.Services.Add( this );
        }


        /// <summary>
        /// Gets the file provider of this referential folder.
        /// </summary>
        public IFileProvider FileProvider { get; }


        /// <summary>
        /// Gets either a <see cref="IFileInfo"/> or a <see cref="IDirectoryContents"/>
        /// for a path.
        /// </summary>
        /// <typeparam name="T">Must be either a IFileInfo or a IDirectoryContent.</typeparam>
        /// <param name="m">Monitor that will emit Fatal if content is not found.</param>
        /// <param name="path">The path for which a IFileInfo or a IDirectoryContent must be obtained.</param>
        /// <returns>A IFileInfo or a IDirectoryContent or null if it is not found.</returns>
        public T ObtainContentReference<T>( IActivityMonitor m, NormalizedPath path )
        {
            bool isFolder = typeof(IDirectoryContents).IsAssignableFrom( typeof(T) );
            if( !isFolder && !typeof( IFileInfo ).IsAssignableFrom( typeof( T ) ) )
            {
                throw new ArgumentException( "Type must be IFileInfo or IDirectoryContent.", nameof( T ) );
            }
            if( !path.IsEmpty )
            {
                if( isFolder )
                {
                    var dir = FileProvider.GetDirectoryContents( path );
                    if( dir.Exists ) return (T)dir;
                    m.Fatal( $"Content file '{path}' not found in {_path}." );
                }
                else
                {
                    var file = FileProvider.GetFileInfo( path );
                    if( file.Exists )
                    {
                        if( !file.IsDirectory ) return (T)file;
                        m.Fatal( $"Reference file '{path}' is actually a directory in {_path}." );
                    }
                    else m.Fatal( $"Reference file '{path}' not found in {_path}." );
                }
            }
            return default( T );
        }
    }
}
