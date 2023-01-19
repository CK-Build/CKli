using CK.Core;
using CK.Env;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CKli
{
    public class XGitFolder : XPathItem
    {
        readonly List<XBranch> _branches;

        public XGitFolder( Initializer initializer, FileSystem fs, XPathItem? parent, World world )
            : base( initializer, fs, parent )
        {
            _branches = new List<XBranch>();
            initializer.ChildServices.Add( this );
            ProtoGitFolder = FileSystem.DeclareProtoGitFolder( world.WorldName, FullPath, Url, world.IsPublicWorld );
        }

        /// <summary>
        /// Gets the ProtoGitFolder object that encapsulates information about Git repository.
        /// </summary>
        public ProtoGitFolder ProtoGitFolder { get; }

        /// <summary>
        /// Gets the url of the remote repository.
        /// </summary>
        [AllowNull]
        public string Url { get; private set; }

        /// <summary>
        /// Gets the develop branch (<see cref="IWorldName.DevelopBranchName"/>).
        /// </summary>
        [AllowNull]
        public XBranch DevelopBranch { get; private set; }

        /// <summary>
        /// Gets all the branches that are defined.
        /// </summary>
        public IReadOnlyList<XBranch> Branches => _branches;

        internal void Register( XBranch b )
        {
            _branches.Add( b );
            if( b.Name == ProtoGitFolder.World.DevelopBranchName )
            {
                DevelopBranch = b;
            }
        }
    }
}
