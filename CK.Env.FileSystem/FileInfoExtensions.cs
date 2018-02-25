using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CK.Env
{
    public static class FileInfoExtensions
    {
        /// <summary>
        /// Reads the content of this file as a string.
        /// Uses standard automatic encoding detection.
        /// </summary>
        /// <param name="this">This file info.</param>
        /// <returns>The text content.</returns>
        public static string ReadAsText( this IFileInfo @this )
        {
            if( @this is TextFileInfo txt ) return txt.Text;
            using( var s = @this.CreateReadStream() )
            using( var t = new StreamReader( s ) )
            {
                return t.ReadToEnd().NormalizeEOL();
            }
        }

        /// <summary>
        /// Checks content equality. This <see cref="IFileInfo"/> and <paramref name="file"/>
        /// must both be actual files, not directories.
        /// </summary>
        /// <param name="this">This file info.</param>
        /// <param name="file">The file to check.</param>
        /// <returns>True of the files have exactly the same content, false otherwise.</returns>
        public static bool ContentEquals( this IFileInfo @this, IFileInfo file )
        {
            if( @this.IsDirectory ) throw new ArgumentException( "Must be a file.", nameof( @this ) );
            if( file == null ) throw new ArgumentNullException( nameof( file ) );
            if( file.IsDirectory ) throw new ArgumentException( "Must be a file.", nameof( file ) );
            if( @this.Length != file.Length ) return false;
            using( var s = @this.CreateReadStream() )
            using( var d = file.CreateReadStream() )
            {
                int read;
                for(; ; )
                {
                    if( s.ReadByte() != (read = d.ReadByte()) ) return false;
                    if( read == -1 ) return true;
                }
            }
        }

        static string[] _textExtensions = new string[] { ".txt", ".cs", ".xml", ".sql" };

        class Origin : ITextFileInfo
        {
            IFileInfo _source;

            public Origin( IFileInfo source )
            {
                Debug.Assert( source.Exists && !source.IsDirectory );
                _source = source;
                TextContent = source.ReadAsText();
            }

            public bool Exists => true;

            public long Length => _source.Length;

            public string PhysicalPath => _source.PhysicalPath;

            public string Name => _source.Name;

            public DateTimeOffset LastModified => _source.LastModified;

            public bool IsDirectory => false;

            public string TextContent { get; }

            public Stream CreateReadStream() => _source.CreateReadStream();
        }

        class Transformed : ITextFileInfo
        {
            IFileInfo _source;
            byte[] _bin;

            public Transformed( IFileInfo source, string t )
            {
                Debug.Assert( source.Exists && !source.IsDirectory );
                Debug.Assert( t != null );
                _source = source;
                TextContent = t;
                _bin = Encoding.UTF8.GetBytes( t );
            }

            public bool Exists => true;

            public long Length => _bin.Length;

            public string PhysicalPath => _source.PhysicalPath;

            public string Name => _source.Name;

            public DateTimeOffset LastModified => _source.LastModified;

            public bool IsDirectory => false;

            public string TextContent { get; }

            public Stream CreateReadStream() => new MemoryStream( _bin );
        }


        public static ITextFileInfo AsTextFileInfo( this IFileInfo f )
        {
            if( f is ITextFileInfo t ) return t;
            if( f == null || !f.Exists || f.IsDirectory ) return null;
            string ext = System.IO.Path.GetExtension( f.Name );
            if( Array.IndexOf( _textExtensions, ext ) < 0 ) return null;
            return new Origin( f );
        }

        public static ITextFileInfo WithText( this ITextFileInfo f, string text )
        {
            return new Transformed( f, text );
        }


    }
}
