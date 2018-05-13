using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public enum GlobalGitStatus
    {
        /// <summary>
        /// Unknow status.
        /// </summary>
        Unknwon,

        /// <summary>
        /// All GitFolders are on <see cref="IWorldName.LocalBranchName"/>.
        /// </summary>
        LocalBranch,

        /// <summary>
        /// All GitFolders are on <see cref="IWorldName.DevelopBranchName"/>.
        /// </summary>
        DevelopBranch

    }

}
