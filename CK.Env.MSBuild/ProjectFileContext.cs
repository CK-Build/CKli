using CK.Core;
using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    public class ProjectFileContext
    {
        /// <summary>
        /// Traits are used to manage framework names.
        /// </summary>
        static readonly public CKTraitContext Traits = new CKTraitContext( "ProjectFileContext" );


        /// <summary>
        /// Defines import of a <see cref="File"/> from another File.
        /// </summary>
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

        /// <summary>
        /// Project Xml file.
        /// </summary>
        public class File
        {
            IReadOnlyList<File> _allFiles;
            bool _hasChanged;

            /// <summary>
            /// Gets the file path.
            /// </summary>
            public NormalizedPath Path { get; }

            public XDocument Document { get; }

            /// <summary>
            /// Gets all the imports in this file.
            /// </summary>
            public IReadOnlyList<Import> Imports { get; }

            /// <summary>
            /// Gets a list of all the imported files without duplicate, starting with this one.
            /// </summary>
            public IReadOnlyList<File> AllFiles => _allFiles;

            /// <summary>
            /// Gets all the root elements from <see cref="AllFiles"/>.
            /// </summary>
            public IEnumerable<XElement> AllRoots => _allFiles.Select( f => f.Document.Root );

            internal int Version { get; }

            internal File( NormalizedPath p, XDocument d, List<Import> imports, int v )
            {
                Path = p;
                Document = d;
                Imports = imports;
                Version = v;
                Document.Changed += OnDocumentChanged;
            }

            void OnDocumentChanged( object sender, XObjectChangeEventArgs e )
            {
                _hasChanged = true;
            }

            /// <summary>
            /// Gets whether the Xml document has changed and should
            /// be saved.
            /// </summary>
            public bool IsModified => _hasChanged;

            /// <summary>
            /// Saves this file if it has been modified as well as all modified imported files.
            /// </summary>
            /// <param name="m">The monitor.</param>
            /// <param name="fs">The file system.</param>
            /// <returns>True on success, false on success.</returns>
            public bool SaveModifiedFile( IActivityMonitor m, FileSystem fs )
            {
                if( _hasChanged )
                {
                    if( !fs.CopyTo( m, Document.ToString(), Path ) ) return false;
                    _hasChanged = false;
                }
                foreach( var i in Imports )
                {
                    if( !i.ImportedFile.SaveModifiedFile( m, fs ) ) return false;
                }
                return true;
            }

            internal void Initialize()
            {
                var start = new File[] { this };
                _allFiles = start.Concat( Imports.SelectMany( i => i.ImportedFile.AllFiles ) )
                                 .Distinct().ToArray();
            }
        }

        readonly IFileProvider _fileSystem;
        readonly Dictionary<NormalizedPath, File> _files;
        int _version;

        /// <summary>
        /// Initializes a new project file context.
        /// </summary>
        /// <param name="fileSystem">The file system provider.</param>
        public ProjectFileContext( IFileProvider fileSystem )
        {
            _fileSystem = fileSystem;
            _files = new Dictionary<NormalizedPath, File>();
        }

        /// <summary>
        /// Gets the file system.
        /// </summary>
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
                    f.Initialize();
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
