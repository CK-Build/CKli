using System;

namespace CK.Env
{
    /// <summary>
    /// Defines the 3 standard git status that applies to one or multiple repositories.
    /// A repository (or a world of repositories) is either on <see cref="Develop"/> (the default),
    /// on <see cref="Local"/> or the <see cref="Master"/>.
    /// Any other configurations results in a <see cref="Unknown"/> status and when a world or a repository
    /// is in this unknown status some operations cannot be done.
    /// </summary>
    [Flags]
    public enum StandardGitStatus
    {
        /// <summary>
        /// Unknown status.
        /// </summary>
        Unknown = 0,

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
        DevelopOrLocal = Local | Develop,

        /// <summary>
        /// On master or develop branch.
        /// </summary>
        MasterOrDevelop = Master | Develop,

        /// <summary>
        /// On master or local branch.
        /// </summary>
        MasterOrLocal = Master | Local,

        /// <summary>
        /// On any of the 3 standard branches.
        /// </summary>
        KnownBranches = Master | Develop | Local
    }

}
