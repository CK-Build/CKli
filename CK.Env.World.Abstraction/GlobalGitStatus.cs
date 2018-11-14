using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Defines the two stable global git status: either all the repositories in a world
    /// are on <see cref="DevelopBranch"/> (the default) or on the <see cref="LocalBranch"/>.
    /// Any other configurations results in a <see cref="Unknwon"/> status and when a world
    /// is in this unknown status no global operation can be done.
    /// </summary>
    public enum GlobalGitStatus
    {
        /// <summary>
        /// Unknow status.
        /// </summary>
        Unknwon,

        /// <summary>
        /// All Git repositories are on <see cref="IWorldName.LocalBranchName"/>.
        /// </summary>
        LocalBranch,

        /// <summary>
        /// All Git repositories are on <see cref="IWorldName.DevelopBranchName"/>.
        /// </summary>
        DevelopBranch

    }

}
