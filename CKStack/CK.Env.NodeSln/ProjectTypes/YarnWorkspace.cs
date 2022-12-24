using CK.Core;
using System;

namespace CK.Env.NodeSln
{
    public class YarnWorkspace : NodeProjectBase
    {
        public YarnWorkspace( NodeSolution solution, NormalizedPath path, NormalizedPath outputPath, int index )
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

        }
    }


}


