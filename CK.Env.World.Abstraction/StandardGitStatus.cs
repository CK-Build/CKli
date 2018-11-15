using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Defines the two standard git status that applies to one or multiple repositories.
    /// Either all the repositories in a world are on <see cref="DevelopBranch"/> (the default) or
    /// on the <see cref="LocalBranch"/>.
    /// Any other configurations results in a <see cref="Unknwon"/> status and when a world
    /// is in this unknown status some operations cannot be done.
    /// </summary>
    public enum StandardGitStatus
    {
        /// <summary>
        /// Unknow status.
        /// </summary>
        Unknwon,

        /// <summary>
        /// On <see cref="IWorldName.LocalBranchName"/>.
        /// </summary>
        LocalBranch,

        /// <summary>
        /// On <see cref="IWorldName.DevelopBranchName"/>.
        /// </summary>
        DevelopBranch

    }

}
