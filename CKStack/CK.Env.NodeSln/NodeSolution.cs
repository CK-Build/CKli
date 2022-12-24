using CK.Core;
using System.Collections.Generic;
using System;
using System.Xml.Linq;
using System.Reflection.Metadata;
using System.Diagnostics;

namespace CK.Env.NodeSln
{
    public class NodeSolution
    {
        readonly List<NodeProjectBase> _projects;

        public NodeSolution( FileSystem fs, NormalizedPath solutionFolderPath )
        {
            FileSystem = fs;
            SolutionFolderPath = solutionFolderPath;
            _projects = new List<NodeProjectBase>();
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
        /// Gets all projects, regardless of their type.
        /// </summary>
        public IReadOnlyList<NodeProjectBase> AllProjects => _projects;

        /// <summary>
        /// Reads or creates a new <see cref="SolutionFile"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="ctx">The project file context.</param>
        /// <param name="solutionPath">The path to the .sln file relative to the <see cref="MSProjContext.FileSystem"/>.</param>
        /// <param name="mustExist">False to allow the file to not exist.</param>
        /// <returns>
        /// The solution file or null on error (for example when not found and <paramref name="mustExist"/> is true).
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
                monitor.Trace( $"No NodeSolution element is '{repositoryInfoPath}'." );
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
                var sPath = p.Attribute( "Path" )?.Value;
                if( sPath == null )
                {
                    monitor.Warn( $"NodeSolution element '{p}' requires a Path attribute. It is ignored." );
                    continue;
                }
                var path = new NormalizedPath( sPath ).ResolveDots( throwOnAboveRoot: false );
                if( path.IsEmptyPath )
                {
                    monitor.Warn( $"NodeSolution element '{p}': Path is empty. It is ignored." );
                    continue;
                }
                var folder = fs.GetFileInfo( nodeSolution.SolutionFolderPath.Combine( path ) );
                if( !folder.Exists || !folder.IsDirectory )
                {
                    if( path.IsEmptyPath )
                    {
                        monitor.Warn( $"NodeSolution element '{p}': Path folder not found. It is ignored." );
                        continue;
                    }
                }
                var sOutputPath = p.Attribute( "OutputPath" )?.Value;
                var outputPath = new NormalizedPath( sOutputPath ).ResolveDots( throwOnAboveRoot: false );
                if( string.IsNullOrWhiteSpace( outputPath ) )
                {
                    monitor.Warn( $"NodeSolution element '{p}' has no or empty OutputPath attribute. It will use the project's Path." );
                    outputPath = path;
                }
                NodeProjectBase? project;
                switch( kind )
                {
                    case NodeProjectKind.YarnWorkspace:
                        project = new YarnWorkspace( nodeSolution, path, outputPath, index++ );
                        break;
                    case NodeProjectKind.AngularWorkspace:
                        project = new AngularWorkspace( nodeSolution, path, outputPath, index++ );
                        break;
                    default:
                        Debug.Assert( kind == NodeProjectKind.NodeProject );
                        project = new NodeProject( nodeSolution, path, outputPath, index++ );
                        break;
                }
                if( project.Initialize( monitor ) )
                {
                    nodeSolution._projects.Add( project );
                }
                else
                {
                    monitor.Warn( $"Project '{project}' initialization failed. It is ignored." );
                    --index;
                }
            }
            return nodeSolution;
        }

    }

}


