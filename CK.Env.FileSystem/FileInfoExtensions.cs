using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;

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
            if( @this is ITextFileInfo txt ) return txt.TextContent;
            using( var s = @this.CreateReadStream() )
            using( var t = new StreamReader( s ) )
            {
                return t.ReadToEnd().NormalizeEOL();
            }
        }

        public static IEnumerable<string> ReadAsTextLines( this IFileInfo @this )
        {
            using( var t = @this is ITextFileInfo txt
                                ? (TextReader)new StringReader( txt.TextContent )
                                : new StreamReader( @this.CreateReadStream() ) )
            {
                string line;
                while( (line = t.ReadLine()) != null ) yield return line;
            }
        }

        public static XDocument ReadAsXDocument( this IFileInfo @this )
        {
            using( var s = @this.CreateReadStream() )
            {
                return XDocument.Load( s );
            }
        }

        public static byte[] ReadAllBytes( this IFileInfo @this )
        {
            if( @this is Transformed t ) return t.BinContent;
            using( var s = @this.CreateReadStream() )
            {
                var b = new byte[@this.Length];
                s.Read( b, 0, b.Length );
                return b;
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
                return ContentEquals( s, d );
            }
        }

        public static bool ContentEquals( Stream s1, Stream s2 )
        {
            int read;
            for(; ; )
            {
                if( s1.ReadByte() != (read = s2.ReadByte()) ) return false;
                if( read == -1 ) return true;
            }
        }

        static string[] _textExtensions = new string[]
        {
            ".txt",
            ".cs", ".js", ".sql",
            ".sln", ".csproj", ".proj",
            ".yml", ".json", ".xml"
        };

        class Origin : ITextFileInfo
        {
            readonly IFileInfo _source;
            string _text;

            public Origin( IFileInfo source )
            {
                Debug.Assert( source.Exists && !source.IsDirectory );
                _source = source;
            }

            public bool Exists => true;

            public long Length => _source.Length;

            public string PhysicalPath => _source.PhysicalPath;

            public string Name => _source.Name;

            public DateTimeOffset LastModified => _source.LastModified;

            public bool IsDirectory => false;

            public string TextContent => _text ?? (_text = _source.ReadAsText());

            public Stream CreateReadStream() => _source.CreateReadStream();
        }

        class Transformed : ITextFileInfo
        {
            readonly ITextFileInfo _source;
            readonly Func<string, string> _trans;

            string _text;
            byte[] _bin;

            public Transformed( ITextFileInfo source, Func<string,string> trans )
            {
                Debug.Assert( source.Exists && !source.IsDirectory );
                Debug.Assert( trans != null );
                _source = source;
                _trans = trans;
            }

            public bool Exists => true;

            public long Length => BinContent.Length;

            public string PhysicalPath => _source.PhysicalPath;

            public string Name => _source.Name;

            public DateTimeOffset LastModified => _source.LastModified;

            public bool IsDirectory => false;

            public string TextContent => _text ?? (_text = _trans( _source.TextContent ) );

            public byte[] BinContent => _bin ?? (_bin = Encoding.UTF8.GetBytes( TextContent ));

            public Stream CreateReadStream() => new MemoryStream( BinContent );
        }

        /// <summary>
        /// Creates a <see cref="ITextFileInfo"/> for this file info if it is possible:
        /// the file exists, is not a directory and its extension denotes a text format.
        /// </summary>
        /// <param name="f">This file info.</param>
        /// <param name="ignoreExtension">
        /// True to ignore the extension.
        /// By default only extensions that are known to contain text are considered.
        /// </param>
        /// <returns>A ITextFileInfo or null.</returns>
        public static ITextFileInfo AsTextFileInfo( this IFileInfo f, bool ignoreExtension = false )
        {
            if( f is ITextFileInfo t ) return t;
            if( f == null || !f.Exists || f.IsDirectory ) return null;
            if( !ignoreExtension )
            {
                string ext = System.IO.Path.GetExtension( f.Name );
                if( Array.IndexOf( _textExtensions, ext ) < 0 ) return null;
            }
            return new Origin( f );
        }

        public static ITextFileInfo WithTransformedText( this ITextFileInfo f, Func<string,string> trans )
        {
            return new Transformed( f, trans );
        }


    }
}
