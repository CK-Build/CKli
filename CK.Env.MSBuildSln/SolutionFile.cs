using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env.MSBuildSln
{
    public partial class SolutionFile : ISolutionItem
    {
        readonly Dictionary<string, ProjectBase> _projectIndex;
        readonly List<ProjectBase> _projectBaseList;
        readonly List<MSProject> _projectMSList;
        readonly Dictionary<string, Section> _sections;
        readonly List<string> _headers;
        bool _isDirtyProjectFiles;
        bool _isDirtyStructure;

        public SolutionFile( FileSystem fs, NormalizedPath filePath )
        {
            FileSystem = fs;
            FilePath = filePath;
            SolutionFolderPath = FilePath.RemoveLastPart();
            _headers = new List<string>();
            _projectIndex = new Dictionary<string, ProjectBase>( StringComparer.OrdinalIgnoreCase );
            _projectBaseList = new List<ProjectBase>();
            _projectMSList = new List<MSProject>();
            _sections = new Dictionary<string, Section>( StringComparer.OrdinalIgnoreCase );
            StandardDotnetToolConfigFile = new DotnetToolConfigFile( this, SolutionFolderPath.AppendPart( ".config" ).AppendPart( "dotnet-tools.json" ) );
        }

        /// <summary>
        /// Gets the file system object.
        /// </summary>
        public FileSystem FileSystem { get; }

        /// <summary>
        /// Gets the .sln path (relative to the <see cref="FileSystem"/>).
        /// </summary>
        public NormalizedPath FilePath { get; }

        /// <summary>
        /// Gets the folder path (relative to the <see cref="FileSystem"/>).
        /// </summary>
        public NormalizedPath SolutionFolderPath { get; }

        /// <summary>
        /// Gets a mutable list of headers.
        /// </summary>
        public IReadOnlyList<string> Headers => _headers;

        /// <summary>
        /// Adds a header in <see cref="Headers"/>.
        /// </summary>
        /// <param name="header">Header to add.</param>
        /// <param name="idx">Optional index where the header must be inserted.</param>
        public void AddHeader( string header, int idx = -1 )
        {
            if( idx < 0 ) idx = _headers.Count;
            _headers.Insert( idx, header );
            SetDirtyStructure( true );
        }

        /// <summary>
        /// Gets all the projects, including <see cref="SolutionFolder"/> projects.
        /// </summary>
        public IReadOnlyCollection<ProjectBase> AllProjects => _projectBaseList;

        /// <summary>
        /// Gets all the actual <see cref="Project"/>, excluding <see cref="SolutionFolder"/>.
        /// </summary>
        public IEnumerable<Project> Projects => _projectBaseList.OfType<Project>();

        /// <summary>
        /// Gets the list of <see cref="MSProject"/>.
        /// This is a subset of <see cref="Projects"/> that we know how to handle.
        /// </summary>
        public IReadOnlyList<MSProject> MSProjects => _projectMSList;

        /// <summary>
        /// Finds a project by its <see cref="Project.ProjectName"/> or <see cref="Project.SolutionRelativePath"/>.
        /// </summary>
        /// <param name="key">The name or relative path.</param>
        /// <returns>The project or null if not found.</returns>
        public ProjectBase FindProject( string key ) => _projectIndex.GetValueWithDefault( key, null );

        internal void AddProject( ProjectBase p )
        {
            Debug.Assert( p.Solution == this );
            _projectIndex.Add( p.ProjectGuid, p );
            _projectIndex.Add( p.SolutionRelativePath, p );
            _projectBaseList.Add( p );
            if( p is MSProject v ) _projectMSList.Add( v );
            SetDirtyStructure( true );
        }

        /// <summary>
        /// Gets the global sections.
        /// </summary>
        public IReadOnlyCollection<Section> GlobalSections => _sections.Values;

        /// <summary>
        /// Adds a global section.
        /// </summary>
        /// <param name="item">Section to add.</param>
        public void AddGlobalSection( Section item )
        {
            _sections.Add( item.Name, item );
            SetDirtyStructure( true );
        }

        /// <summary>
        /// Finds a global section. 
        /// </summary>
        /// <param name="name">Name of the section.</param>
        /// <returns>The section or null if not found.</returns>
        public Section FindGlobalSection( string name ) => _sections.GetValueWithDefault( name, null );

        /// <summary>
        /// Gets the <see cref="DotnetToolConfigFile"/> that is the ".config/dotnet-tools.json" file
        /// relatively to this solution file.
        /// It may not exist.
        /// </summary>
        public DotnetToolConfigFile StandardDotnetToolConfigFile { get; }

        /// <summary>
        /// Gets the projects that directly belongs to this solution (no intermediate
        /// <see cref="KnownProjectType.SolutionFolder"/>).
        /// </summary>
        public IEnumerable<ProjectBase> Children
        {
            get
            {
                foreach( var project in AllProjects )
                {
                    if( project.ParentFolder == null )
                    {
                        yield return project;
                    }
                }
            }
        }

        internal ProjectBase FindProjectByGuid( IActivityMonitor m, string guid, int lineNumber = 0 )
        {
            var project = FindProject( guid );
            if( project == null )
            {
                m.Error( lineNumber == 0
                            ? $"Unable to find the project '{guid}' in solution file."
                            : $"Guid '{guid}' defined on line #{lineNumber} not found in the solution." );
            }
            return project;
        }

        internal T FindProjectByGuid<T>( IActivityMonitor m, string guid, int lineNumber = 0 ) where T : ProjectBase
        {
            var project = FindProjectByGuid( m, guid );
            if( project == null ) return null;
            if( project is T typed ) return typed;
            m.Error( lineNumber == 0
                        ? $"Project '{guid}' must be a {typeof( T ).GetType().Name}."
                        : $"Guid '{guid}' defined on line #{lineNumber} references must be a {typeof( T ).GetType().Name}." );
            return null;
        }

        /// <summary>
        /// Gets whether any of the the projects this solution contains need to be saved or the <see cref="StandardDotnetToolConfigFile"/>
        /// or the solution itself needs to be saved.
        /// </summary>
        public bool IsDirty => _isDirtyProjectFiles || _isDirtyStructure || StandardDotnetToolConfigFile.IsDirty;

        /// <summary>
        /// Raised whenever a <see cref="Save"/> has actually been made
        /// and <see cref="IsDirty"/> is now false.
        /// </summary>
        public event EventHandler<EventMonitoredArgs> Saved;

        internal void CheckDirtyProjectFiles( bool shouldBeDirty )
        {
            if( _isDirtyProjectFiles != shouldBeDirty )
            {
                _isDirtyProjectFiles = MSProjects.Any( p => p.IsDirty );
            }
        }

        internal void SetDirtyStructure( bool dirty )
        {
            _isDirtyStructure = dirty;
        }

        /// <summary>
        /// Saves all files that have been modified.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Save( IActivityMonitor m )
        {
            bool saved = false;
            if( _isDirtyProjectFiles )
            {
                foreach( var p in MSProjects )
                {
                    if( !p.Save( m ) ) return false;
                }
                CheckDirtyProjectFiles( false );
                saved = true;
            }
            if( _isDirtyStructure )
            {
                using( var w = new System.IO.StringWriter() )
                {
                    Write( w );
                    FileSystem.CopyTo( m, w.ToString(), FilePath );
                }
                saved = true;
            }
            if( StandardDotnetToolConfigFile.IsDirty )
            {
                StandardDotnetToolConfigFile.Save( m );
                saved = true;
            }
            if( saved ) Saved?.Invoke( this, new EventMonitoredArgs( m ) );
            return true;
        }

        SolutionFile ISolutionItem.Solution => this;

        public override string ToString() => FilePath;

    }
}
