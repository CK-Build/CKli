using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Defines the two standard git status that applies to one or multiple repositories.
    /// A repository (or a world of repositories) is either on <see cref="DevelopBranch"/> (the default) or
    /// on the <see cref="LocalBranch"/>.
    /// Any other configurations results in a <see cref="Unknwon"/> status and when a world or a repository
    /// is in this unknown status some operations cannot be done.
    /// </summary>
    [Flags]
    public enum StandardGitStatus
    {
        /// <summary>
        /// Unknow status.
        /// </summary>
        Unknwon = 0,

        /// <summary>
        /// On <see cref="IWorldName.LocalBranchName"/>.
        /// </summary>
        LocalBranch = 1,

        /// <summary>
        /// On <see cref="IWorldName.DevelopBranchName"/>.
        /// </summary>
        DevelopBranch = 2,

        /// <summary>
        /// On local or develop branch.
        /// </summary>
        DevelopOrLocalBranch = LocalBranch|DevelopBranch
    }

}
