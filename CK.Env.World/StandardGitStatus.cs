using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Defines the 3 standard git status that applies to one or multiple repositories.
    /// A repository (or a world of repositories) is either on <see cref="Develop"/> (the default),
    /// on <see cref="Local"/> or the <see cref="Master"/>.
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
        Local = 1,

        /// <summary>
        /// On <see cref="IWorldName.DevelopBranchName"/>.
        /// </summary>
        Develop = 2,

        /// <summary>
        /// On <see cref="IWorldName.MasterBranchName"/>.
        /// </summary>
        Master = 4,

        /// <summary>
        /// On develop or local branch.
        /// </summary>
        DevelopOrLocalBranch = Local|Develop,

        /// <summary>
        /// On master or develop branch.
        /// </summary>
        MasterOrDevelopBranch = Master | Develop,

        /// <summary>
        /// On master or local branch.
        /// </summary>
        MasterOrLocalBranch = Master | Local,

        /// <summary>
        /// On any of the 3 standard branches.
        /// </summary>
        KnownBranches = Master | Develop | Local
    }

}
