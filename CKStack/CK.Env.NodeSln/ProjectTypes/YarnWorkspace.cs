using CK.Core;
using System;

namespace CK.Env.NodeSln
{

    public sealed class YarnWorkspace : NodeRootProjectBase
    {
        internal YarnWorkspace( NodeSolution solution, NormalizedPath path, NormalizedPath outputPath, int index )
            : base( solution, path, outputPath, index )
        {
        }

        internal override bool Initialize( IActivityMonitor monitor )
        {
            if( !base.Initialize( monitor ) ) return false;
            if( !PackageJsonFile.HasWorkspaces )
            {
                monitor.Error( $"Invalid '{PackageJsonFile.FilePath}' for a YarnWorkspace: a \"workspaces\": [\"*\"] property is required." );
                return false;
            }
            // Loads all subordinate projects.

            return true;
        }

    }


}


