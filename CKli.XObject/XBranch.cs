using CK.Core;

namespace CKli
{
    /// <summary>
    /// Defines a Git branch.
    /// A branch usually contains a <see cref="XSolutionSpec"/>.
    /// </summary>
    public class XBranch : XPathItem
    {
        public XBranch(
            Initializer initializer,
            XGitFolder parent )
            : base( initializer, parent.FileSystem, parent.FullPath.AppendPart( "branches" ) )
        {
            initializer.ChildServices.Add( this );
            parent.Register( this );
        }

        /// <summary>
        /// Gets the parent <see cref="XGitFolder"/> object.
        /// </summary>
        public new XGitFolder Parent => (XGitFolder)base.Parent;

    }
}
