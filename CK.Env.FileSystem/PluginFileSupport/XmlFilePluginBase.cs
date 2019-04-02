using CK.Core;
using CK.Text;
using System.Xml.Linq;

namespace CK.Env.Plugins
{
    /// <summary>
    /// Exposes a <see cref="XDocument"/> from a file.
    /// </summary>
    public abstract class XmlFilePluginBase : XmlFileBase, IGitBranchPlugin
    {
        readonly GitBranchPluginImpl _pluginImpl;

        public XmlFilePluginBase( GitFolder f, NormalizedPath branchPath, NormalizedPath filePath )
            : base( f.FileSystem, filePath )
        {
            if( !filePath.StartsWith( branchPath ) ) throw new ArgumentException( $"Path {filePath} must start with folder {f.SubPath}." );
            _pluginImpl = new GitBranchPluginImpl( f, branchPath );
        }


        /// <summary>
        /// Gets the Git folder.
        /// Its <see cref="GitFolder.CurrentBranchName"/> can be different from
        /// the branch of the plugin (see <see cref="BranchPath"/>).
        /// </summary>
        public GitFolder Folder => _pluginImpl.Folder;

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
