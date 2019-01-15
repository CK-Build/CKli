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
        /// The <see cref="NormalizedPath.LastPart"/> is the actual branch name.
        /// </summary>
        NormalizedPath BranchPath { get; }

        /// <summary>
        /// Gets the standard plugin branch name into which this plugin is registered.
        /// It is <see cref="StandardGitStatus.Unknown"/> if the actual branch is not one
        /// the 3 standard ones.
        /// </summary>
        StandardGitStatus PluginBranch { get; }
    }

    /// <summary>
    /// Offers helpers of <see cref="IGitBranchPlugin"/>.
    /// </summary>
    public static class GitBranchPluginExtension
    {
        /// <summary>
        /// Checks that the <see cref="GitFolder.CurrentBranchName"/> name is the same as the plugin <see cref="IGitBranchPlugin.BranchPath"/>.
        /// </summary>
        /// <param name="this">This branch plugin.</param>
        /// <param name="m">The monitor to use.</param>
        /// <param name="traceError">False to not trace an error and simply returning false.</param>
        /// <returns>True if this plugin is on the same branch as the Git folder, false otherwise.</returns>
        public static bool CheckCurrentBranch( this IGitBranchPlugin @this, IActivityMonitor m, bool traceError = true )
        {
            if( @this.BranchPath.LastPart != @this.Folder.CurrentBranchName )
            {
                if( traceError ) m.Error( $"[{@this.GetType().Name}] Current branch is {@this.Folder.CurrentBranchName}. Must be in {@this.BranchPath.LastPart}." );
                return false;
            }
            return true;
        }
    }
}
