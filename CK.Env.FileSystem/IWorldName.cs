using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Defines the "Long Term Support World". A World is identified by a name
    /// and a <see cref="LTSKey"/>.
    /// </summary>
    public interface IWorldName
    {
        /// <summary>
        /// Gets tne name of this world.
        /// </summary>
        string WorldName { get; }

        /// <summary>
        /// Gets the LTS key. Normalized to null for current.
        /// </summary>
        string LTSKey { get; }

        /// <summary>
        /// Gets the develop branch name.
        /// </summary>
        string DevelopBranchName { get; }

        /// <summary>
        /// Gets the master branch name.
        /// </summary>
        string MasterBranchName { get; }

        /// <summary>
        /// Gets the develop local branch name.
        /// </summary>
        string DevelopLocalBranchName { get; }

    }
}
