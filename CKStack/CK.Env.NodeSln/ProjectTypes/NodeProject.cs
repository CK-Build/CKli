using CK.Core;
using System;

namespace CK.Env.NodeSln
{
    public class NodeProject : NodeProjectBase
    {
        public NodeProject( NodeSolution solution, NormalizedPath path, NormalizedPath outputPath, int index )
            : base( solution, path, outputPath, index )
        {
        }

        internal static NodeProject? Create( IActivityMonitor monitor,
                                             NodeSolution nodeSolution,
                                             NormalizedPath path,
                                             NormalizedPath outputPath,
                                             int index )
        {
            var p = new NodeProject( nodeSolution, path, outputPath, index );
            return p.Initialize( monitor ) ? p : null;
        }

    }

}


