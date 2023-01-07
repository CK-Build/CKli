using CK.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Angular workspace: projects are defined by angular.json file.
    /// </summary>
    public sealed class AngularWorkspace : NodeRootProjectBase, INodeWorkspace
    {
        readonly List<NodeSubProject> _subProjects;

        internal AngularWorkspace( NodeSolution solution, NormalizedPath path, NormalizedPath outputPath )
            : base( solution, path, outputPath )
        {
            _subProjects = new List<NodeSubProject>();
        }

        /// <inheritdoc />
        public IReadOnlyList<NodeSubProject> Projects => _subProjects;

        private protected override bool DoSave( IActivityMonitor monitor )
        {
            foreach( var p in _subProjects )
            {
                if( !p.Save( monitor ) ) return false;
            }
            return true;
        }

        internal override bool Initialize( IActivityMonitor monitor )
        {
            if( !base.Initialize( monitor ) ) return false;
            JObject? jProjects = TryReadAngularJsonProjects( monitor );
            if( jProjects == null ) return false;
            foreach( var propProject in jProjects.Properties() )
            {
                var name = propProject.Name;
                var root = propProject.Value["root"]?.ToString();
                if( string.IsNullOrWhiteSpace( root ) )
                {
                    monitor.Warn( $"Project '{name}' is missing \"root\" property. It is ignored." );
                }
                else
                {
                    var project = Solution.TryReadProject( monitor, NodeProjectKind.NodeSubProject, this, root, null, $"AngularWorkspace '{Path}'" );
                    if( project == null )
                    {
                        monitor.Error( $"Unable to load Angular project '{root}'." );
                        return false;
                    }
                    Debug.Assert( project is NodeSubProject );
                    _subProjects.Add( (NodeSubProject)project );
                }
            }
            return true;
        }

        JObject? TryReadAngularJsonProjects( IActivityMonitor monitor )
        {
            bool success = true;
            if( !PackageJsonFile.IsPrivate )
            {
                monitor.Error( $"An Angular workspace project should be private. File '{PackageJsonFile.FilePath}' must be fixed with a \"private\": true property." );
                success = false;
            }
            var aPath = Path.AppendPart( "angular.json" );
            var angularFile = Solution.FileSystem.GetFileInfo( aPath );
            if( !angularFile.Exists || angularFile.IsDirectory )
            {
                monitor.Error( $"Missing Angular workspace file '{aPath}'." );
            }
            else
            {
                JObject angularJson = angularFile.ReadAsJObject();
                if( angularJson["projects"] is JObject jProjects )
                {
                    if( success ) return jProjects;
                }
                monitor.Error( $"Missing \"projects\" in '{aPath}'." );
            }
            return null;
        }
    }


}


