using CK.Core;

namespace CK.Env
{
    struct GitBranchPluginImpl
    {
        /// <summary>
        /// Initializes a new plugin kernel implementation for a branch.
        /// </summary>
        /// <param name="f">The folder.</param>
        /// <param name="branchPath">The actual branch.</param>
        public GitBranchPluginImpl( GitRepository f, NormalizedPath branchPath )
        {
            if( branchPath.LastPart == f.World.LocalBranchName ) StandardPluginBranch = StandardGitStatus.Local;
            else if( branchPath.LastPart == f.World.DevelopBranchName ) StandardPluginBranch = StandardGitStatus.Develop;
            else if( branchPath.LastPart == f.World.MasterBranchName ) StandardPluginBranch = StandardGitStatus.Master;
            else StandardPluginBranch = StandardGitStatus.Unknown;
            BranchPath = branchPath;
            Folder = f;
        }

        /// <summary>
        /// Gets the Git folder.
        /// Its <see cref="GitRepository.CurrentBranchName"/> can be different from
        /// the branch of the plugin (see <see cref="BranchPath"/>).
        /// </summary>
        public GitRepository Folder { get; }

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
        public StandardGitStatus StandardPluginBranch { get; }
    }
}
