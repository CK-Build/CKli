
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

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
            using( var s = CheckFileInfoExists( @this ).CreateReadStream() )
            using( var t = new StreamReader( s ) )
            {
                return t.ReadToEnd().ReplaceLineEndings();
            }
        }

        static IFileInfo CheckFileInfoExists( IFileInfo @this )
        {
            if( !@this.Exists )
            {
                throw new InvalidOperationException( $"File info '{@this.Name}' (PhysicalPath: {@this.PhysicalPath}) doesn't exist." );
            }
            return @this;
        }

        /// <summary>
        /// Reads this file as a text (uses a <see cref="StreamReader"/>) as multiple
        /// lines. This must exist and be a file otherwise exceptions will be thrown.
        /// </summary>
        /// <param name="this">This file info.</param>
        /// <returns>The lines.</returns>
        public static IEnumerable<string> ReadAsTextLines( this IFileInfo @this )
        {
            using( var t = @this is ITextFileInfo txt
                                ? (TextReader)new StringReader( txt.TextContent )
                                : new StreamReader( CheckFileInfoExists( @this ).CreateReadStream() ) )
            {
                string? line;
                while( (line = t.ReadLine()) != null ) yield return line;
            }
        }

        public static YamlMappingNode ReadAsYaml( this IFileInfo @this )
        {
            using( var t = @this is ITextFileInfo txt
                                ? (TextReader)new StringReader( txt.TextContent )
                                : new StreamReader( CheckFileInfoExists( @this ).CreateReadStream() ) )
            {
                var deserialiser = new Deserializer();
                return deserialiser.Deserialize<YamlMappingNode>( t ) ?? new YamlMappingNode();
            }
        }


        public static JObject ReadAsJObject( this IFileInfo @this )
        {
            return JObject.Parse( @this.ReadAsText() );
        }

        public static XDocument ReadAsXDocument( this IFileInfo @this )
        {
            using( var s = CheckFileInfoExists( @this ).CreateReadStream() )
            {
                return XDocument.Load( s );
            }
        }

        public static byte[] ReadAllBytes( this IFileInfo @this )
        {
            if( @this is Transformed t ) return t.BinContent;
            using( var s = CheckFileInfoExists( @this ).CreateReadStream() )
            {
                var b = new byte[@this.Length];
                s.Read( b, 0, b.Length );
                return b;
            }
        }

        static readonly string[] _textExtensions = new string[]
        {
            ".txt",
            ".cs", ".js", ".sql",
            ".sln", ".csproj", ".proj",
            ".yml", ".json", ".xml",
            ".editorconfig", ".config"
        };

        class Origin : ITextFileInfo
        {
            readonly IFileInfo _source;
            string? _text;

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

            public Transformed( ITextFileInfo source, Func<string, string> trans )
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

            public string TextContent => _text ?? (_text = _trans( _source.TextContent ));

            public byte[] BinContent => _bin ?? (_bin = Encoding.UTF8.GetBytes( TextContent ));

            public Stream CreateReadStream() => new MemoryStream( BinContent );
        }

        /// <summary>
        /// Creates a <see cref="ITextFileInfo"/> for this file info if it is possible:
        /// the file exists, is not a directory and its extension denotes a text format.
        /// Returns null otherwise.
        /// </summary>
        /// <param name="f">This file info.</param>
        /// <param name="ignoreExtension">
        /// True to ignore the extension.
        /// By default only extensions that are known to contain text are considered.
        /// </param>
        /// <returns>A ITextFileInfo or null.</returns>
        public static ITextFileInfo? AsTextFileInfo( this IFileInfo f, bool ignoreExtension = false )
        {
            if( f is ITextFileInfo t ) return t;
            if( f == null || !f.Exists || f.IsDirectory ) return null;
            if( !ignoreExtension )
            {
                string ext = Path.GetExtension( f.Name );
                if( Array.IndexOf( _textExtensions, ext ) < 0 ) return null;
            }
            return new Origin( f );
        }

        public static ITextFileInfo WithTransformedText( this ITextFileInfo f, Func<string, string> trans )
        {
            return new Transformed( f, trans );
        }


    }
}
