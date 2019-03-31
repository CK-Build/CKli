using CK.Core;
using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{

    /// <summary>
    /// Central root class that manages <see cref="Solution"/> loading and caching.
    /// </summary>
    public class MSBuildContext
    {
        /// <summary>
        /// Traits are used to manage framework names.
        /// The <see cref="CKTraitContext.Separator"/> is the ';' to match the one used by csproj (parsing and
        /// string representation becomes straightforward).
        /// </summary>
        static readonly public CKTraitContext Traits = new CKTraitContext( "ProjectFileContext", ';' );

        /// <summary>
        /// Defines import of a <see cref="ProjectFile"/> from another File.
        /// </summary>
        public struct Import
        {
            public readonly XElement ImportElement;
            public readonly ProjectFile ImportedFile;

            internal Import( XElement e, ProjectFile f )
            {
                ImportElement = e;
                ImportedFile = f;
            }
        }

        /// <summary>
        /// Package.json file.
        /// </summary>
        public sealed class NpmPackageFile
        {
            bool _hasChanged;

            /// <summary>
            /// Gets the file path in the <see cref="FileSystem"/>.
            /// </summary>
            public NormalizedPath Path { get; }

            public JObject Document { get; }
        }

        /// <summary>
        /// Project Xml file.
        /// </summary>
        public sealed class ProjectFile
        {
            IReadOnlyList<ProjectFile> _allFiles;
            bool _hasChanged;

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
            public IReadOnlyList<ProjectFile> AllFiles => _allFiles;

            internal ProjectFile( NormalizedPath p, XDocument d, List<Import> imports )
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
                var start = new ProjectFile[] { this };
                _allFiles = start.Concat( Imports.SelectMany( i => i.ImportedFile.AllFiles ) )
                                 .Distinct().ToArray();
            }
        }

        internal interface ISolutionTracker
        {
            Solution CurrentVersion { get; }
            Solution LastVersion { get; }
        }

        class SolutionTracker : ISolutionTracker
        {
            Solution _last;
            bool _loaded;

            public bool IsLoaded => _loaded;

            public Solution CurrentVersion => _loaded ? _last : null;

            public Solution LastVersion => _last;

            public Solution OnUnload( MSBuildContext ctx )
            {
                Debug.Assert( _loaded );
                _loaded = false;
                foreach( var f in _last.AllProjects.SelectMany( p => p.ProjectFile.AllFiles ) )
                {
                    ctx.UnloadFile( f.Path );
                }
                return _last;
            }

            public void OnLoad( Solution newOne )
            {
                Debug.Assert( !_loaded && newOne != null );
                _last = newOne;
                _loaded = true;
                newOne.SetTracker( this );
            }
        }

        readonly Dictionary<NormalizedPath, ProjectFile> _files;
        readonly Dictionary<NormalizedPath, SolutionTracker> _solutions;

        /// <summary>
        /// Initializes a new project file context.
        /// </summary>
        /// <param name="fileSystem">The file system provider.</param>
        public MSBuildContext( FileSystem fileSystem )
        {
            FileSystem = fileSystem;
            _files = new Dictionary<NormalizedPath, ProjectFile>();
            _solutions = new Dictionary<NormalizedPath, SolutionTracker>();
        }

        /// <summary>
        /// Gets the file system.
        /// </summary>
        public FileSystem FileSystem { get; }

        /// <summary>
        /// Returns a newly loaded <see cref="Solution"/> or retrieves it from the cache.
        /// Null if the solution doesn't exist.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="path">The path in the <see cref="FileSystem"/>.</param>
        /// <param name="settings"></param>
        /// <param name="primary">The primary solution if this solution is a secondary solution.</param>
        /// <param name="npmProjects">Optional list of expected NPM projects.</param>
        /// <param name="type">The special secondary solution type.</param>
        /// <param name="force">True to force the reload of the solution.</param>
        /// <returns>The solution and whether it has been loaded or obtained from the cache. Null if the solution doesn't exist.</returns>
        public (Solution Solution, bool Loaded) FindOrLoadSolution(
            IActivityMonitor m,
            NormalizedPath path,
            Solution primary = null,
            IEnumerable<NpmProjectDescription> npmProjects = null,
            SolutionSpecialType type = SolutionSpecialType.None,
            bool force = false )
        {
            if( primary == null && type != SolutionSpecialType.None ) throw new ArgumentException( $"Primary solution, type must be None." );
            if( primary != null && type == SolutionSpecialType.None ) throw new ArgumentException( $"Secondary solution, type must be IncludedSecondarySolution or IndependantSecondarySolution." );
            if( primary != null && primary.Current != primary ) throw new ArgumentException( $"Primary solution is not the current one." );

            SolutionTracker tracker = null;
            Solution s;
            if( _solutions.TryGetValue( path, out tracker )
                && tracker.IsLoaded
                && !force )
            {
                s = tracker.LastVersion;
                // Check that it is the same kind of solution.
                // If not, reloads it.
                if( (primary == null && s.PrimarySolution == null)
                    || (primary != null && s.PrimarySolution == primary && type == s.SpecialType) )
                {
                    return (s, false);
                }
            }
            s = DoLoad( m, path, npmProjects, tracker );
            if( s != null )
            {
                if( primary != null ) s.SetAsSecondarySolution( primary, type );
                return (s, true);
            }
            return (null, false);
        }

        Solution DoLoad(
            IActivityMonitor m,
            NormalizedPath path,
            IEnumerable<NpmProjectDescription> npmProjects,
            SolutionTracker tracker )
        {
            using( m.OpenTrace( $"Loading solution {path}." ) )
            {
                if( tracker != null && tracker.IsLoaded )
                {
                    var previous = tracker.OnUnload( this );
                    foreach( var secondary in previous.LoadedSecondarySolutions )
                    {
                        _solutions[secondary.FilePath].OnUnload( this );
                    }
                }
                try
                {
                    Solution s = Solution.Load( m, this, path, npmProjects ?? Array.Empty<NpmProjectDescription>() );
                    if( s != null )
                    {
                        if( tracker == null )
                        {
                            tracker = new SolutionTracker();
                            _solutions.Add( path, tracker );
                        }
                        tracker.OnLoad( s );
                    }
                    return s;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                }
                return null;
            }
        }

        internal ProjectFile FindOrLoadProjectFile( IActivityMonitor m, NormalizedPath path )
        {
            if( _files.TryGetValue( path, out ProjectFile f )  )
            {
                return f;
            }
            using( m.OpenTrace( $"Loading project file {path}." ) )
            {
                try
                {
                    XDocument content = FileSystem.GetFileInfo( path ).ReadAsXDocument();
                    var imports = new List<Import>();
                    f = new ProjectFile( path, content, imports );
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
