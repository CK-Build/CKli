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

    public class MSBuildContext
    {
        /// <summary>
        /// Traits are used to manage framework names.
        /// </summary>
        static readonly public CKTraitContext Traits = new CKTraitContext( "ProjectFileContext", ';' );

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
            /// Gets a list starting with this one and all the imported files without duplicate.
            /// </summary>
            public IReadOnlyList<File> AllFiles => _allFiles;

            internal File( NormalizedPath p, XDocument d, List<Import> imports )
            {
                Path = p;
                Document = d;
                Imports = imports;
                Document.Changed += OnDocumentChanged;
            }

            void OnDocumentChanged( object sender, XObjectChangeEventArgs e )
            {
                _hasChanged = true;
            }

            /// <summary>
            /// Gets whether any <see cref="AllFiles"/>' <see cref="Document"/> has changed.
            /// </summary>
            public bool IsDirty => _allFiles.Any( f => f._hasChanged );

            /// <summary>
            /// Saves this file if it has been modified as well as all modified imported files.
            /// </summary>
            /// <param name="m">The monitor.</param>
            /// <param name="fs">The file system.</param>
            /// <returns>True on success, false on error.</returns>
            internal bool Save( IActivityMonitor m, FileSystem fs )
            {
                if( _hasChanged )
                {
                    if( !fs.CopyTo( m, Document.ToString(), Path ) ) return false;
                    _hasChanged = false;
                }
                foreach( var i in Imports )
                {
                    if( !i.ImportedFile.Save( m, fs ) ) return false;
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

        readonly FileSystem _fileSystem;
        readonly Dictionary<NormalizedPath, File> _files;
        readonly Dictionary<NormalizedPath, Solution> _solutions;

        /// <summary>
        /// Initializes a new project file context.
        /// </summary>
        /// <param name="fileSystem">The file system provider.</param>
        public MSBuildContext( FileSystem fileSystem )
        {
            _fileSystem = fileSystem;
            _files = new Dictionary<NormalizedPath, File>();
            _solutions = new Dictionary<NormalizedPath, Solution>();
        }

        /// <summary>
        /// Gets the file system.
        /// </summary>
        public FileSystem FileSystem => _fileSystem;

        public (Solution Solution, bool Loaded) FindOrLoadSolution( IActivityMonitor m, NormalizedPath path, Solution primary = null, SolutionSpecialType type = SolutionSpecialType.None, bool force = false )
        {
            if( primary == null && type != SolutionSpecialType.None ) throw new ArgumentException( $"Primary solution, type must be None." );
            if( primary != null && type == SolutionSpecialType.None ) throw new ArgumentException( $"Secodary solution, type must be IncludedSecondarySolution or IndependantSecondarySolution." );
            if( !force && _solutions.TryGetValue( path, out Solution s ) )
            {
                if( (primary == null && s.PrimarySolution == null)
                    || (primary != null && s.PrimarySolution == primary && type == s.SpecialType) )
                {
                    return (s, false);
                }
            }
            s = DoLoad( m, path );
            if( s != null )
            {
                if( primary != null ) s.SetAsSecondarySolution( primary, type );
                return (s, true);
            }
            return (null, false);
        }

        Solution DoLoad( IActivityMonitor m, NormalizedPath path )
        {
            using( m.OpenTrace( $"Loading solution {path}." ) )
            {
                if( _solutions.TryGetValue( path, out Solution previous ) )
                {
                    _solutions.Remove( path );
                    if( previous.LoadedSecodarySolutions != null )
                    {
                        foreach( var secondary in previous.LoadedSecodarySolutions )
                        {
                            _solutions.Remove( secondary.FilePath );
                        }
                    }
                }
                try
                {
                    Solution s = Solution.Load( m, this, path );
                    if( s != null ) _solutions.Add( path, s );
                    return s;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                }
                return null;
            }
        }

        internal File FindOrLoadProjectFile( IActivityMonitor m, NormalizedPath path )
        {
            if( _files.TryGetValue( path, out File f )  )
            {
                return f;
            }
            using( m.OpenTrace( $"Loading project file {path}." ) )
            {
                try
                {
                    XDocument content = _fileSystem.GetFileInfo( path ).ReadAsXDocument();
                    var imports = new List<Import>();
                    f = new File( path, content, imports );
                    _files[path] = f;
                    var folder = path.RemoveLastPart();
                    imports.AddRange( content.Root.Descendants( "Import" )
                                        .Select( i => (E: i, P: (string)i.Attribute( "Project" )) )
                                        .Where( i => i.P != null )
                                        .Select( i => new Import( i.E, FindOrLoadProjectFile( m, folder.Combine( i.P ).ResolveDots() ) ) ) );
                    f.Initialize();
                    return f;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    _files.Remove( path );
                    return null;
                }
            }
        }

        internal bool UnloadFile( NormalizedPath path ) => _files.Remove( path );
    }
}
