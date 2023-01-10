using CK.Core;
using System.Collections.Generic;
using System;
using System.Xml.Linq;
using System.Reflection.Metadata;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Node solution is defined in the RepositoryInfo.xml file and contains
    /// <see cref="NodeProject"/>, <see cref="YarnWorkspace"/> and/or <see cref="AngularWorkspace"/>.
    /// </summary>
    public class NodeSolution
    {
        readonly List<NodeRootProjectBase> _projects;
        readonly bool _useYarn;
        bool _isDirty;

        public NodeSolution( FileSystem fs, NormalizedPath solutionFolderPath )
        {
            FileSystem = fs;
            SolutionFolderPath = solutionFolderPath;
            _projects = new List<NodeRootProjectBase>();
        }

        /// <summary>
        /// Gets the file system object.
        /// </summary>
        public FileSystem FileSystem { get; }

        /// <summary>
        /// Gets the folder path (relative to the <see cref="FileSystem"/>):
        /// this is the root of the repository.
        /// </summary>
        public NormalizedPath SolutionFolderPath { get; }

        /// <summary>
        /// Gets whether a ".yarn" folder exists at the 
        /// </summary>
        public bool UseYarn => _useYarn;

        /// <summary>
        /// Gets the root projects, regardless of their type.
        /// </summary>
        public IReadOnlyList<NodeRootProjectBase> RootProjects => _projects;

        /// <summary>
        /// Gets all the projects. Workspaces are followed by their <see cref="NodeSubProject"/>.
        /// </summary>
        public IEnumerable<NodeProjectBase> AllProjects
        {
            get
            {
                foreach( var r in _projects )
                {
                    yield return r;
                    if( r is INodeWorkspace w )
                    {
                        foreach( var p in w.Projects )
                        {
                            yield return p;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets whether any of the projects in this solution needs to be saved.
        /// </summary>
        public bool IsDirty => _isDirty;

        internal void SetDirty() => _isDirty = true;

        /// <summary>
        /// Raised whenever a <see cref="Save"/> has actually been made
        /// and <see cref="IsDirty"/> is now false.
        /// </summary>
        public event EventHandler<EventMonitoredArgs>? Saved;

        /// <summary>
        /// Saves all files that have been modified.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="callRestoreDependencies">True to call <see cref="RestoreDependencies(IActivityMonitor)"/> after a successful save.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Save( IActivityMonitor monitor, bool callRestoreDependencies = true )
        {
            if( _isDirty )
            {
                foreach( var p in _projects )
                {
                    if( !p.Save( monitor ) )
                    {
                        return false;
                    }
                }
                _isDirty = false;
                if( callRestoreDependencies && !RestoreDependencies( monitor ) )
                {
                    return false;
                }
                Saved?.Invoke( this, new EventMonitoredArgs( monitor ) );
            }
            return true;
        }

        /// <summary>
        /// Calls "yarn" or "npm install" to restore the packages on all projects where <see cref="NodeRootProjectBase.RestoreRequired"/> is true.
        /// Does nothing otherwise.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool RestoreDependencies( IActivityMonitor monitor )
        {
            foreach( var p in _projects )
            {
                if( !p.RestoreDependencies( monitor ) ) return false;
            }
            return true;
        }

        /// <summary>
        /// Reads a new <see cref="NodeSolution"/> from a RepositoryInfo.xml file.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="fs">The file system.</param>
        /// <param name="repositoryInfoPath">The path to the RepositoryInfo.xml file relative to the <see cref="FileSystem"/>.</param>
        /// <returns>
        /// The node solution that has at least one project or null. Whether errors occurred must be handled by handling logs (typically by
        /// using <see cref="ActivityMonitorExtension.OnError(IActivityMonitor, Action)"/>).
        /// </returns>
        public static NodeSolution? Read( IActivityMonitor monitor, FileSystem fs, NormalizedPath repositoryInfoPath )
        {
            var file = fs.GetFileInfo( repositoryInfoPath );
            if( !file.Exists || file.IsDirectory )
            {
                monitor.Warn( $"File '{repositoryInfoPath}' not found. Unable to read the Node solution." );
                return null;
            }
            XElement? eSolution = null;
            using( var s = file.CreateReadStream() )
            {
                eSolution = XDocument.Load( s, LoadOptions.None )?.Root?.Element( "NodeSolution" );
            }
            if( eSolution == null )
            {
                monitor.Trace( $"No NodeSolution element in '{repositoryInfoPath}'." );
                return null;
            }
            var nodeSolution = new NodeSolution( fs, repositoryInfoPath.RemoveLastPart() );
            foreach( var p in eSolution.Elements() )
            {
                if( !Enum.TryParse<NodeProjectKind>( p.Name.LocalName, out var kind ) )
                {
                    monitor.Warn( $"Unknown NodeSolution element name '{p.Name.LocalName}'. It is ignored." );
                    continue;
                }
                if( kind == NodeProjectKind.NodeSubProject )
                {
                    monitor.Warn( $"A NodeSolution cannot contain 'NodeSubProject'. It is ignored." );
                    continue;
                }
                var sPath = p.Attribute( "Path" )?.Value;
                if( sPath == null )
                {
                    monitor.Warn( $"NodeSolution element '{p}' requires a Path attribute. It is ignored." );
                    continue;
                }
                var path = new NormalizedPath( sPath ).ResolveDots( throwOnAboveRoot: false );
                var project = nodeSolution.TryReadProject( monitor,
                                                           kind,
                                                           null,
                                                           path,
                                                           $"NodeSolution element '{p}'" );
                if( project is NodeRootProjectBase root )
                {
                    LogCheckPackageManager( monitor, root );
                    nodeSolution._projects.Add( root );
                }
            }
            if( nodeSolution.RootProjects.Count == 0 )
            {
                monitor.Error( $"Unable to read at least one Node project from '{repositoryInfoPath}'." );
                nodeSolution = null;
            }

            return nodeSolution;
        }

        static void LogCheckPackageManager( IActivityMonitor monitor, NodeRootProjectBase root )
        {
            if( root.Solution._projects.Count == 0 )
            {
                if( root.UseYarn )
                {
                    monitor.Info( $"'{root}' project uses yarn since .yarn/ folder found: {root.YarnPath}. All other projects in this repository should be the same." );
                }
                else
                {
                    monitor.Info( $"'{root}' project uses NPM. All other projects should be the same. All other projects in this repository should also use NPM." );
                }
            }
            else
            {
                var first = root.Solution._projects[0];
                if( first.UseYarn != root.UseYarn )
                {
                    if( root.UseYarn )
                    {
                        monitor.Warn( $"{root} uses yarn ({root.YarnPath})." );
                    }
                    else
                    {
                        monitor.Warn( $"{root} uses NPM." );
                    }
                }
                else if( first.YarnPath != root.YarnPath )
                {
                    monitor.Warn( $"{root} uses yarn but it's .yarn is '{root.YarnPath}'." );
                }
            }
        }

        /// <summary>
        /// Tries to read a project.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="kind">The kind of project that must be read.</param>
        /// <param name="workspace">Workspace (<paramref name="kind"/> is necessarily <see cref="NodeProjectKind.NodeSubProject"/></param>
        /// <param name="path">Project path.</param>
        /// <param name="ownerDescription">The caller description (for logs).</param>
        /// <returns></returns>
        internal NodeProjectBase? TryReadProject( IActivityMonitor monitor,
                                                  NodeProjectKind kind,
                                                  INodeWorkspace? workspace,
                                                  NormalizedPath path,
                                                  string ownerDescription )
        {
            Debug.Assert( (workspace != null) == (kind == NodeProjectKind.NodeSubProject) );
            if( path.IsEmptyPath )
            {
                monitor.Warn( $"{ownerDescription}: Path is empty. It is ignored." );
                return null;
            }
            var root = workspace != null ? SolutionFolderPath.Combine( workspace.Path ) : SolutionFolderPath;
            var folder = FileSystem.GetFileInfo( root.Combine( path ) );
            if( !folder.Exists || !folder.IsDirectory )
            {
                if( path.IsEmptyPath )
                {
                    monitor.Warn( $"{ownerDescription}: Path folder not found. It is ignored." );
                    return null;
                }
            }
            NodeProjectBase? project;

            if( workspace == null )
            {
                switch( kind )
                {
                    case NodeProjectKind.YarnWorkspace:
                        project = new YarnWorkspace( this, path );
                        break;
                    case NodeProjectKind.AngularWorkspace:
                        project = new AngularWorkspace( this, path );
                        break;
                    default:
                        Debug.Assert( kind == NodeProjectKind.NodeProject );
                        project = new NodeProject( this, path );
                        break;
                }
            }
            else
            {
                project = new NodeSubProject( this, workspace, path );
            }
            if( project.Initialize( monitor ) )
            {
                return project;
            }
            monitor.Warn( $"Project '{project}' initialization failed. It is ignored." );
            return null;

        }
    }

}


