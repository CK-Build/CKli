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
        /// Gets the folder path (relative to the <see cref="FileSystem"/>).
        /// </summary>
        public NormalizedPath SolutionFolderPath { get; }

        /// <summary>
        /// Gets the root projects, regardless of their type.
        /// </summary>
        public IReadOnlyList<NodeRootProjectBase> Projects => _projects;

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
        /// The node solution or null. Whether errors occurred must be handled by handling logs (typically by
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
            int index = 0;
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
                                                           p.Attribute( "OutputPath" )?.Value,
                                                           ref index,
                                                           $"NodeSolution element '{p}'" );
                if( project is NodeRootProjectBase r )
                {
                    nodeSolution._projects.Add( r );
                }
            }
            return nodeSolution;
        }

        /// <summary>
        /// Tries to read a project.
        /// </summary>
        /// <param name="monitor">The monitor.</param>
        /// <param name="kind">The kind of project that must be read.</param>
        /// <param name="workspace">Workspace (<paramref name="kind"/> is necessarily <see cref="NodeProjectKind.NodeSubProject"/></param>
        /// <param name="path">Project path.</param>
        /// <param name="outputPath">Optional project output path for root projects only (when <paramref name="workspace"/> is null).</param>
        /// <param name="index">Index of the project in its holder.</param>
        /// <returns></returns>
        internal NodeProjectBase? TryReadProject( IActivityMonitor monitor,
                                                  NodeProjectKind kind,
                                                  INodeWorkspace? workspace,
                                                  NormalizedPath path,
                                                  string? outputPath,
                                                  ref int index,
                                                  string ownerDescription )
        {
            Debug.Assert( (workspace == null) == (kind == NodeProjectKind.NodeSubProject) );
            Debug.Assert( outputPath == null || workspace != null );
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
                var outPath = root.Combine( outputPath ).ResolveDots( throwOnAboveRoot: false );
                if( string.IsNullOrWhiteSpace( outPath ) )
                {
                    monitor.Warn( $"{ownerDescription} has no or empty OutputPath attribute. It will use the project's Path." );
                    outPath = path;
                }
                switch( kind )
                {
                    case NodeProjectKind.YarnWorkspace:
                        project = new YarnWorkspace( this, path, outPath, index++ );
                        break;
                    case NodeProjectKind.AngularWorkspace:
                        project = new AngularWorkspace( this, path, outPath, index++ );
                        break;
                    default:
                        Debug.Assert( kind == NodeProjectKind.NodeProject );
                        project = new NodeProject( this, path, outPath, index++ );
                        break;
                }
            }
            else
            {
                project = new NodeSubProject( this, workspace, path, index++ );
            }
            if( project.Initialize( monitor ) )
            {
                return project;
            }
            monitor.Warn( $"Project '{project}' initialization failed. It is ignored." );
            --index;
            return null;
        }
    }

}


