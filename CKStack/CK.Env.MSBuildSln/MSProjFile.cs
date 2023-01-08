using CK.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        public readonly struct Import
        {
            /// <summary>
            /// The file path.
            /// </summary>
            public readonly NormalizedPath Path;

            /// <summary>
            /// Gets the import Xml element.
            /// Null if this is an implicit import like Directory.Build.props above the project. 
            /// </summary>
            public readonly XElement? ImportElement;

            /// <summary>
            /// Gets whether <see cref="ImportElement"/> is null: this is an implicit import
            /// like Directory.Build.props above the project.
            /// </summary>
            [MemberNotNullWhen(false, nameof(ImportElement))]
            public bool IsImplicitImport => ImportElement == null;

            /// <summary>
            /// The imported file. Can be null if the file has not been found.
            /// </summary>
            public readonly MSProjFile? ImportedFile;

            internal Import( XElement? e, NormalizedPath path, MSProjFile? f )
            {
                ImportElement = e;
                Path = path;
                ImportedFile = f;
            }

            public override string ToString() => ImportElement?.ToString() ?? $"Implicit: {Path}";
        }

        IReadOnlyList<MSProjFile> _allFiles;
        List<Import> _imports;
        bool _hasChanged;

        MSProjFile( NormalizedPath p, XDocument d, List<Import> imports )
        {
            Path = p;
            Document = d;
            _imports = imports;
            Document.Changed += OnDocumentChanged;
        }

        /// <summary>
        /// Finds from the cache or loads a Xml project file.
        /// </summary>
        /// <param name="fs">The <see cref="FileSystem"/>.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="path">The file path relative to <paramref name="fs"/>.</param>
        /// <param name="cache">Cache by path.</param>
        /// <param name="warnIfNotFound">False to avoid a warning if the file doesn't exist.</param>
        /// <returns>The file or null if unable to load it.</returns>
        public static MSProjFile? FindOrLoadProjectFile( FileSystem fs,
                                                         IActivityMonitor monitor,
                                                         NormalizedPath path,
                                                         Dictionary<NormalizedPath, MSProjFile> cache,
                                                         bool warnIfNotFound = true )
        {
            if( cache.TryGetValue( path, out MSProjFile? f ) )
            {
                return f;
            }
            var fP = fs.GetFileInfo( path );
            if( fP.IsDirectory ) return null;
            if( !fP.Exists )
            {
                if( warnIfNotFound )
                {
                    monitor.Warn( $"Unable to find project file '{path}'. This project is ignored. This may be a case sensitivity issue!" );
                }
                return null;
            }
            using( monitor.OpenTrace( $"Loading project file '{path}'." ) )
            {
                try
                {
                    XDocument content = fP.ReadAsXDocument();
                    Debug.Assert( content.Root != null );
                    // Put the new file in cache before expanding the imports
                    // to handle cycles.
                    var imports = new List<Import>();
                    f = new MSProjFile( path, content, imports );
                    cache[path] = f;
                    var folder = path.RemoveLastPart();
                    // Resolves the explicit imports.
                    imports.AddRange( content.Root.Descendants( "Import" )
                                        .Where( i => i.Attribute( "Sdk" ) == null )
                                        .Select( i => (E: i, P: (string?)i.Attribute( "Project" )) )
                                        .Where( i => i.P != null )
                                        .Select( i => (i.E, P: folder.Combine( i.P ).ResolveDots()) )
                                        .Select( i => new Import( i.E, i.P, FindOrLoadProjectFile( fs, monitor, i.P, cache ) ) ) );

                    f.UpdateAllFilesList();
                    return f;
                }
                catch( Exception ex )
                {
                    monitor.Error( ex );
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
        public IReadOnlyList<Import> Imports => _imports;

        /// <summary>
        /// Removes an explicit import.
        /// </summary>
        /// <param name="import"></param>
        /// <returns>True on success, false otherwise.</returns>
        public bool RemoveImport( Import import )
        {
            Throw.CheckArgument( !import.IsImplicitImport );
            return DoRemoveAt( _imports.IndexOf( i => i.ImportElement == import.ImportElement ) );
        }

        /// <summary>
        /// Adds an implicit import.
        /// This has no impact on <see cref="IsDirty"/>.
        /// </summary>
        /// <param name="import">The imported file.</param>
        /// <returns>True if file has been added, false if it already existed.</returns>
        public bool AddImplicitImport( MSProjFile implicitImport )
        {
            Throw.CheckNotNullArgument( implicitImport );
            if( !_imports.Any( i => i.ImportedFile == implicitImport ) )
            {
                _imports.Add( new Import( null, implicitImport.Path, implicitImport ) );
                UpdateAllFilesList();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes any explicit import that satisfies the selector.
        /// </summary>
        /// <param name="selector">Returns true to remove the import, false to keep it.</param>
        public void RemoveImports( Func<Import, bool> selector )
        {
            int i = 0;
            while( i < _imports.Count )
            {
                var import = _imports[i];
                if( !import.IsImplicitImport && selector( import ) )
                {
                    DoRemoveAt( i );
                }
                else
                {
                    ++i;
                }
            }
        }

        bool DoRemoveAt( int idx )
        {
            if( idx < 0 ) return false;
            var i = _imports[idx];
            Debug.Assert( !i.IsImplicitImport );
            i.ImportElement.Remove();
            Debug.Assert( _hasChanged, "The OnDocumentChanged did its job." );
            _imports.RemoveAt( idx );
            UpdateAllFilesList();
            return true;
        }

        /// <summary>
        /// Gets a list starting with this one and all the imported files without duplicate.
        /// This list contains the explicit and implicit imported files.
        /// </summary>
        public IReadOnlyList<MSProjFile> AllFiles => _allFiles;

        void OnDocumentChanged( object? sender, XObjectChangeEventArgs e )
        {
            _hasChanged = true;
        }

        /// <summary>
        /// Gets whether any <see cref="AllFiles"/>' <see cref="Document"/> has changed.
        /// </summary>
        public bool IsDirty => _allFiles.Any( f => f._hasChanged );

        /// <summary>
        /// Finds all &lt;PropertyGroup&gt; ... &lt;<paramref name="propertyName"/> ... &gt; elements
        /// in <see cref="AllFiles"/>.
        /// </summary>
        /// <param name="propertyName">The property name to lookup.</param>
        /// <returns>The list of elements.</returns>
        public IList<XElement> FindProperty( string propertyName ) => _allFiles.Select( f => f.Document.Root )
                                                                                .Elements( "PropertyGroup" )
                                                                                .Elements()
                                                                                .Where( x => x.Name.LocalName == propertyName )
                                                                                .ToList();

        /// <summary>
        /// Finds all &lt;PropertyGroup&gt; property element with a name that matches a predicate
        /// in <see cref="AllFiles"/>.
        /// </summary>
        /// <param name="elementNameFilter">The element name predicate.</param>
        /// <returns>The list of elements.</returns>
        public IList<XElement> FindProperty( Func<XName, bool> elementNameFilter ) => _allFiles.Select( f => f.Document.Root )
                                                                                               .Elements( "PropertyGroup" )
                                                                                               .Elements()
                                                                                               .Where( x => elementNameFilter( x.Name ) )
                                                                                               .ToList();

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

        internal void UpdateAllFilesList()
        {
            _allFiles = Imports.Where( i => i.ImportedFile != null ).SelectMany( i => i.ImportedFile!.AllFiles )
                        .Append( this )
                        .Distinct()
                        .ToArray();
        }


    }

}
