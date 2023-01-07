using CK.Core;
using CommunityToolkit.HighPerformance;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using static CK.Core.AsyncLock;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Basic Node project. Everything is defined by the package.json manifest.
    /// </summary>
    public sealed class NodeProject : NodeRootProjectBase
    {
        internal NodeProject( NodeSolution solution, NormalizedPath path, NormalizedPath outputPath )
            : base( solution, path, outputPath )
        {
        }

        internal override bool Initialize( IActivityMonitor monitor )
        {
            if( !base.Initialize( monitor ) ) return false;
            if( PackageJsonFile.Workspaces.Count > 0 )
            {
                monitor.Error( $"Invalid '{PackageJsonFile.FilePath}' for a NodeProject: a \"workspaces\": [...] property MUST NOT appear." );
                return false;
            }
            return true;
        }

        private protected override bool DoSave( IActivityMonitor monitor ) => true;

    }

}


