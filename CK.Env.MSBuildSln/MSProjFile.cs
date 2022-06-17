using CK.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// Project Xml file.
    /// </summary>
    public sealed class MSProjFile
    {

        /// <summary>
        /// Defines import of a <see cref="MSProjFile"/> from another File.
        /// </summary>
        public struct Import
        {
            /// <summary>
            /// Gets the import Xml element.
            /// </summary>
            public readonly XElement ImportElement;

            /// <summary>
            /// The imported file. Can be null if the file has not been found.
            /// </summary>
            public readonly MSProjFile ImportedFile;

            internal Import( XElement e, MSProjFile f )
            {
                ImportElement = e;
                ImportedFile = f;
            }

            public override string ToString() => ImportElement.ToString();
        }

        IReadOnlyList<MSProjFile> _allFiles;
        bool _hasChanged;

        MSProjFile( NormalizedPath p, XDocument d, List<Import> imports )
        {
            Path = p;
            Document = d;
            Imports = imports;
            Document.Changed += OnDocumentChanged;
        }

        /// <summary>
        /// Finds from the cache or loads a Xml project file.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="path">The file path relative to the <see cref="FileSystem"/>.</param>
        /// <param name="cache">Cache by path.</param>
        /// <returns>The file or null if unable to load it.</returns>
        public static MSProjFile FindOrLoadProjectFile(
            FileSystem fs,
            IActivityMonitor m,
            NormalizedPath path,
            Dictionary<NormalizedPath, MSProjFile> cache )
        {
            if( cache.TryGetValue( path, out MSProjFile f ) )
            {
                return f;
            }
            using( m.OpenTrace( $"Loading project file {path}." ) )
            {
                try
                {
                    var fP = fs.GetFileInfo( path );
                    if( !fP.Exists )
                    {
                        m.Warn( $"Unable to find project file '{path}'. This project is ignored. This may be a case sensivity issue!" );
                        return null;
                    }
                    XDocument content = fP.ReadAsXDocument();
                    var imports = new List<Import>();
                    f = new MSProjFile( path, content, imports );
                    cache[path] = f;
                    var folder = path.RemoveLastPart();
                    imports.AddRange( content.Root.Descendants( "Import" )
                                        .Where( i => i.Attribute( "Sdk" ) == null )
                                        .Select( i => (E: i, P: (string)i.Attribute( "Project" )) )
                                        .Where( i => i.P != null )
                                        .Select( i => new Import( i.E, FindOrLoadProjectFile( fs, m, folder.Combine( i.P ).ResolveDots(), cache ) ) ) );

                    f.Initialize();
                    return f;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    cache.Remove( path );
                    return null;
                }
            }
        }


        /// <summary>
        /// Gets the file path in the <see cref="FileSystem"/>.
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
        public IReadOnlyList<MSProjFile> AllFiles => _allFiles;

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
                if( i.ImportedFile?.Save( m, fs ) == false ) return false;
            }
            return true;
        }

        internal void Initialize()
        {
            var start = new MSProjFile[] { this };
            _allFiles = start
                .Concat(
                    Imports.Where( i => i.ImportedFile != null )
                    .SelectMany( i => i.ImportedFile.AllFiles ) )
                .Distinct()
                .ToArray();
        }


    }

}
