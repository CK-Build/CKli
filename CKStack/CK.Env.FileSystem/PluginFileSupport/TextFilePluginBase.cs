using CK.Core;
using System;
using System.Text;

#nullable enable

namespace CK.Env.Plugin
{
    /// <summary>
    /// Basic base class for text files.
    /// </summary>
    public abstract class TextFilePluginBase : TextFileBase, IGitBranchPlugin
    {
        readonly GitBranchPluginImpl _pluginImpl;

        protected TextFilePluginBase( GitRepository f, NormalizedPath branchPath, NormalizedPath filePath, Encoding? encoding = null )
            : base( f.FileSystem, filePath, encoding )
        {
            if( !filePath.StartsWith( branchPath ) ) throw new ArgumentException( $"Path {filePath} must start with folder {f.SubPath}." );
            _pluginImpl = new GitBranchPluginImpl( f, branchPath );
        }

        /// <summary>
        /// Gets the Git folder.
        /// Its <see cref="GitRepository.CurrentBranchName"/> can be different from
        /// the branch of the plugin (see <see cref="BranchPath"/>).
        /// </summary>
        public GitRepository GitFolder => _pluginImpl.Folder;

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
        public StandardGitStatus StandardPluginBranch => _pluginImpl.StandardPluginBranch;
    }
}
