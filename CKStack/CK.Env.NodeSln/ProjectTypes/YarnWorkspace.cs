using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CK.Env.NodeSln
{

    public sealed class YarnWorkspace : NodeRootProjectBase, INodeWorkspace
    {
        readonly List<NodeSubProject> _subProjects;

        internal YarnWorkspace( NodeSolution solution, NormalizedPath path )
            : base( solution, path )
        {
            _subProjects = new List<NodeSubProject>();
        }


        /// <inheritdoc />
        public IReadOnlyList<NodeSubProject> Projects => _subProjects;

        internal override bool Initialize( IActivityMonitor monitor )
        {
            if( !base.Initialize( monitor ) ) return false;
            bool success = true;
            if( PackageJsonFile.Workspaces.Count == 0 )
            {
                monitor.Error( $"Invalid '{PackageJsonFile.FilePath}' for a YarnWorkspace: a \"workspaces\": [] property with at least 1 subordinated path is required." );
                success = false;
            }
            if( !PackageJsonFile.IsPrivate )
            {
                monitor.Error( $"A Yarn workspace project should be private. File '{PackageJsonFile.FilePath}' must be fixed with a \"private\": true property." );
                success = false;
            }
            if( !success ) return false;
            // Loads all subordinate projects.
            foreach( var p in PackageJsonFile.Workspaces )
            {
                var project = Solution.TryReadProject( monitor, NodeProjectKind.NodeSubProject, this, p, $"YarnWorkspace '{Path}'" );
                if( project == null )
                {
                    monitor.Error( $"Unable to load project '{p}'." );
                    return false;
                }
                Debug.Assert( project is NodeSubProject );
                _subProjects.Add( (NodeSubProject)project );
            }
            return true;
        }

        private protected override bool DoSave( IActivityMonitor monitor )
        {
            foreach( var p in _subProjects )
            {
                if( !p.Save( monitor ) ) return false;
            }
            return true;
        }

    }


}


