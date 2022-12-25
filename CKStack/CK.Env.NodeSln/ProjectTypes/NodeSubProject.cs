using CK.Core;
using System;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Node project subordinated to a <see cref="INodeWorkspace"/>.
    /// Everything is defined by the package.json manifest.
    /// </summary>
    public sealed class NodeSubProject : NodeProjectBase
    {
        readonly INodeWorkspace _workspace;

        internal NodeSubProject( NodeSolution solution, INodeWorkspace workspace, NormalizedPath path, int index )
            : base( solution, path, index )
        {
            _workspace = workspace;
        }

        /// <summary>
        /// Gets the workspace to which this project belongs.
        /// </summary>
        public INodeWorkspace Workspace => _workspace;

        internal override bool Initialize( IActivityMonitor monitor )
        {
            if( !base.Initialize( monitor ) ) return false;
            if( PackageJsonFile.HasWorkspaces )
            {
                monitor.Error( $"Invalid '{PackageJsonFile.FilePath}' for a NodeSubProject: a \"workspaces\": [\"*\"] property MUST NOT appear." );
                return false;
            }
            return true;
        }

    }

}


