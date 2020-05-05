using CK.Text;
using System;
using System.Text;
using System.Xml.Linq;

#nullable enable

namespace CK.Env.Plugin
{
    /// <summary>
    /// Exposes a <see cref="XDocument"/> from a file.
    /// </summary>
    public abstract class XmlFilePluginBase : XmlFileBase, IGitBranchPlugin
    {
        readonly GitBranchPluginImpl _pluginImpl;

        /// <summary>
        /// Initializes a new Xml file plugin.
        /// </summary>
        /// <param name="f">The Git folder.</param>
        /// <param name="branchPath">The branch path in the Git folder.</param>
        /// <param name="filePath">The file path (relative to the <see cref="FileSystem"/>). It must start with the <paramref name="branchPath"/>.</param>
        /// <param name="rootName">The document's root element name. See <see cref="RootName"/>.</param>
        /// <param name="encoding">Optional encoding that defaults to UTF-8.</param>
        public XmlFilePluginBase( GitFolder f, NormalizedPath branchPath, NormalizedPath filePath, XName rootName, Encoding? encoding = null )
            : base( f.FileSystem, filePath, rootName, encoding )
        {
            if( !filePath.StartsWith( branchPath ) ) throw new ArgumentException( $"Path {filePath} must start with folder {f.SubPath}." );
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
