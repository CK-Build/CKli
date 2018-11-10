using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CKli
{
    public class XGitFolder : XPathItem
    {
        readonly XSolutionCentral _central;
        readonly List<XBranch> _branches;

        public XGitFolder(
            Initializer initializer,
            XPathItem parent,
            XSolutionCentral central )
            : base( initializer, parent.FileSystem, parent )
        {
            _branches = new List<XBranch>();
            _central = central;
            central.Register( this );
            initializer.ChildServices.Add( this );
            GitFolder = FileSystem.EnsureGitFolder( initializer.Monitor, central.World, FullPath, Url );
        }

        /// <summary>
        /// Gets the <see cref="GitFolder"/> object that encapsulates the Git repoistory.
        /// </summary>
        public GitFolder GitFolder { get; }

        /// <summary>
        /// Gets the solution central.
        /// </summary>
        public XSolutionCentral SolutionCentral => _central;

        /// <summary>
        /// Gets the url of the remote repository.
        /// </summary>
        public string Url { get; private set; }

        /// <summary>
        /// Gets the develop branch (<see cref="IWorldName.DevelopBranchName"/>).
        /// </summary>
        public XBranch DevelopBranch { get; private set; }

        /// <summary>
        /// Gets all the branches that are defined.
        /// </summary>
        public IReadOnlyList<XBranch> Branches => _branches; 

        internal void Register( XBranch b )
        {
            _branches.Add( b );
            if( b.Name == SolutionCentral.World.DevelopBranchName )
            {
                DevelopBranch = b;
            }
        }
    }
}
