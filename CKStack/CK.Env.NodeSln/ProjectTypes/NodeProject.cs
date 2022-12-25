using CK.Core;
using System;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Basic Node project. Everything is defined by the package.json manifest.
    /// </summary>
    public sealed class NodeProject : NodeRootProjectBase
    {
        internal NodeProject( NodeSolution solution, NormalizedPath path, NormalizedPath outputPath, int index )
            : base( solution, path, outputPath, index )
        {
        }

        internal override bool Initialize( IActivityMonitor monitor )
        {
            if( !base.Initialize( monitor ) ) return false;
            if( PackageJsonFile.HasWorkspaces )
            {
                monitor.Error( $"Invalid '{PackageJsonFile.FilePath}' for a NodeProject: a \"workspaces\": [\"*\"] property MUST NOT appear." );
                return false;
            }
            return true;
        }

    }

}


