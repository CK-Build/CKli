using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Marker interface.
    /// The single public constructor must have a parameter 'NormalizedPath branchPath'.
    /// </summary>
    public interface IGitBranchPlugin : IGitPlugin
    {
        /// <summary>
        /// Gets the branch path (relative to the <see cref="FileSystem"/>) into
        /// which this plugin is registered.
        /// </summary>
        NormalizedPath BranchPath { get; }
    }

    public static class GitBranchPluginExtension
    {
        public static bool CheckCurrentBranch( this IGitBranchPlugin @this, IActivityMonitor m )
        {
            if( @this.BranchPath.LastPart != @this.Folder.CurrentBranchName )
            {
                m.Error( $"[{@this.GetType().Name}] Current branch is {@this.Folder.CurrentBranchName}. Must be in {@this.BranchPath.LastPart}." );
                return false;
            }
            return true;
        }
    }
}
