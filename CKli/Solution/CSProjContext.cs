using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env.Solution
{
    public class CSProjContext
    {
        public struct Import
        {
            public readonly XElement ImportElement;
            public readonly File ImportedFile;

            internal Import( XElement e, File f )
            {
                ImportElement = e;
                ImportedFile = f;
            }
        }

        public class File
        {
            public NormalizedPath Path { get; }

            public XDocument Content { get; }

            public IReadOnlyList<Import> Imports { get; }

            internal File( NormalizedPath p, XDocument d, List<Import> imports )
            {
                Path = p;
                Content = d;
                Imports = imports;
            }
        }

        readonly IFileProvider _fileSystem;
        readonly Dictionary<NormalizedPath, File> _files;

        public CSProjContext( IFileProvider fileSystem )
        {
            _fileSystem = fileSystem;
            _files = new Dictionary<NormalizedPath, File>();
        }

        public File FindOrLoad( NormalizedPath path )
        {
            if( _files.TryGetValue( path, out File f ) ) return f;

            XDocument content = _fileSystem.GetFileInfo( path ).ReadAsXDocument();
            var imports = new List<Import>();
            f = new File( path, content, imports );
            _files.Add( path, f );
            imports.AddRange( content.Root.Descendants( "Import" )
                                .Select( i => ( E: i, P :(string)i.Attribute( "Project" )) )
                                .Where( i => i.P != null )
                                .Select( i => new Import( i.E, FindOrLoad( path.Combine( i.P ).ResolveDots() ) ) ) );
            return f;
        }
    }
}
