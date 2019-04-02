using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    struct GitBranchPluginImpl
    {
        /// <summary>
        /// Initializes a new plugin kernel implemntation for a branch.
        /// </summary>
        /// <param name="f">The folder.</param>
        /// <param name="branchPath">The actual branch.</param>
        public GitBranchPluginImpl( GitFolder f, NormalizedPath branchPath )
        {
            if( branchPath.LastPart == f.World.LocalBranchName ) PluginBranch = StandardGitStatus.Local;
            else if( branchPath.LastPart == f.World.DevelopBranchName ) PluginBranch = StandardGitStatus.Develop;
            else if( branchPath.LastPart == f.World.MasterBranchName ) PluginBranch = StandardGitStatus.Master;
            else PluginBranch = StandardGitStatus.Unknown;
            BranchPath = branchPath;
            Folder = f;
        }

        /// <summary>
        /// Gets the Git folder.
        /// Its <see cref="GitFolder.CurrentBranchName"/> can be different from
        /// the branch of the plugin (see <see cref="BranchPath"/>).
        /// </summary>
        public GitFolder Folder { get; }

        /// <summary>
        /// Gets the branch path (relative to the <see cref="FileSystem"/>) into
        /// which this plugin is registered.
        /// The <see cref="NormalizedPath.LastPart"/> is the actual branch name.
        /// </summary>
        public NormalizedPath BranchPath { get; }

        /// <summary>
        /// Gets the standard plugin branch name into which this plugin is registered.
        /// It is <see cref="StandardGitStatus.Unknown"/> if the actual branch is not one
        /// the 3 standard ones.
        /// </summary>
        public StandardGitStatus PluginBranch { get; }
    }
}
