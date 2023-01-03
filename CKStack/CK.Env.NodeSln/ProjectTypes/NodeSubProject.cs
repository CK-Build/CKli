using CK.Core;
using System;

namespace CK.Env.NodeSln
{
    /// <summary>
    /// Node project subordinated to a <see cref="INodeWorkspace"/>.
    /// Everything is defined by the package.json manifest. This can be a
    /// published project or not (see <see cref="PackageJsonFile.IsPrivate"/>).
    /// <para>
    /// Subordinated projects have no "output path" since it's the responsibility of their
    /// workspace to publish them (when <see cref="PackageJsonFile.IsPrivate"/> is false).
    /// </para>
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
            if( PackageJsonFile.Workspaces.Count > 0 )
            {
                monitor.Error( $"Invalid '{PackageJsonFile.FilePath}' for a NodeSubProject: \"workspaces\": [...] property MUST NOT appear." );
                return false;
            }
            return true;
        }

        internal override void SetDirty( bool restoreRequired )
        {
            base.SetDirty( restoreRequired );
            ((NodeProjectBase)_workspace).SetDirty( restoreRequired );
        }

        private protected override bool DoSave( IActivityMonitor monitor ) => true;

    }

}


