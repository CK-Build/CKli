using CK.Core;
using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env.Solution
{
    public class ProjectFileContext
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

            internal int Version { get; }

            internal File( NormalizedPath p, XDocument d, List<Import> imports, int v )
            {
                Path = p;
                Content = d;
                Imports = imports;
                Version = v;
            }
        }

        readonly IFileProvider _fileSystem;
        readonly Dictionary<NormalizedPath, File> _files;
        int _version;

        public ProjectFileContext( IFileProvider fileSystem )
        {
            _fileSystem = fileSystem;
            _files = new Dictionary<NormalizedPath, File>();
        }

        public IFileProvider FileSystem => _fileSystem;

        /// <summary>
        /// Loads the project file and all its imported parts if not already loaded.
        /// Returns null on error.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="path">The project file path.</param>
        /// <param name="forceLoad">True to ignore any already loaded file content.</param>
        /// <returns>The file on success, null on error.</returns>
        public File FindOrLoad( IActivityMonitor m, NormalizedPath path, bool forceLoad )
        {
            return FindOrLoad( m, path, forceLoad ? ++_version : -1 );
        }

        File FindOrLoad( IActivityMonitor m, NormalizedPath path, int version )
        {
            if( _files.TryGetValue( path, out File f ) && f.Version >= version )
            {
                return f;
            }
            using( m.OpenDebug( $"Loading project file {path}." ) )
            {
                try
                {
                    XDocument content = _fileSystem.GetFileInfo( path ).ReadAsXDocument();
                    var imports = new List<Import>();
                    f = new File( path, content, imports, _version );
                    _files[path] = f;
                    var folder = path.RemoveLastPart();
                    imports.AddRange( content.Root.Descendants( "Import" )
                                        .Select( i => (E: i, P: (string)i.Attribute( "Project" )) )
                                        .Where( i => i.P != null )
                                        .Select( i => new Import( i.E, FindOrLoad( m, folder.Combine( i.P ).ResolveDots(), version ) ) ) );
                    return f;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return null;
                }
            }
        }
    }
}
