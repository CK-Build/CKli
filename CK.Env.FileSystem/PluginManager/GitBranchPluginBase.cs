using CK.Text;

namespace CK.Env
{
    public abstract class GitBranchPluginBase : IGitBranchPlugin
    {
        readonly GitBranchPluginImpl _pluginImpl;

        /// <summary>
        /// Initializes a new plugin for a branch.
        /// </summary>
        /// <param name="f">The folder.</param>
        /// <param name="branchPath">The actual branch.</param>
        protected GitBranchPluginBase( GitFolder f, NormalizedPath branchPath )
        {
            _pluginImpl = new GitBranchPluginImpl( f, branchPath );
        }

        /// <summary>
        /// Gets the Git folder.
        /// Its <see cref="GitFolder.CurrentBranchName"/> can be different from
        /// the branch of the plugin (see <see cref="BranchPath"/>).
        /// </summary>
        public GitFolder GitFolder => _pluginImpl.Folder;

        /// <summary>
        /// Gets the branch path (relative to the <see cref="FileSystem"/>) into
        /// which this plugin is registered.
        /// The <see cref="NormalizedPath.LastPart"/> is the actual branch name.
        /// </summary>
        public NormalizedPath BranchPath => _pluginImpl.BranchPath;

        /// <summary>
        /// Gets the standard plugin branch name into which this plugin is registered.
        /// It is <see cref="StandardGitStatus.Unknown"/> if the actual branch is not one
        /// the 3 standard ones.
        /// </summary>
        public StandardGitStatus PluginBranch => _pluginImpl.PluginBranch;

    }
}
