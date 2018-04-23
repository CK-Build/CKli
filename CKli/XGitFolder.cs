using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using System;

namespace CKli
{
    public class XGitFolder : XPathItem
    {
        public XGitFolder(
            Initializer initializer,
            XPathItem parent,
            GlobalContext.World world,
            IssueCollector issueCollector
            )
            : base( initializer, parent.FileSystem, parent )
        {
            initializer.ChildServices.Add( this );
            GitFolder = FileSystem.EnsureGitFolder( initializer.Monitor, world, FullPath, Url );
        }

        /// <summary>
        /// Gets the <see cref="GitFolder"/> object that encapsulates the Git repoistory.
        /// </summary>
        public GitFolder GitFolder { get; }

        /// <summary>
        /// Gets the rul of the remote repository.
        /// </summary>
        public string Url { get; private set; }
    }
}
