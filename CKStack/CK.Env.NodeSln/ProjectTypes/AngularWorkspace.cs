using CK.Core;
using System;

namespace CK.Env.NodeSln
{
    public class AngularWorkspace : NodeRootProjectBase
    {
        public AngularWorkspace( NodeSolution solution, NormalizedPath path, NormalizedPath outputPath, int index )
            : base( solution, path, outputPath, index )
        {
        }

        internal static NodeProjectBase? Create( IActivityMonitor monitor,
                                                 FileSystem fs,
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


