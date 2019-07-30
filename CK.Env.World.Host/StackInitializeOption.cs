using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Paraletrizes the <see cref="GitWorldStore.Initialize"/> method.
    /// </summary>
    public enum StackInitializeOption
    {
        /// <summary>
        /// Repository is not cloned/opened.
        /// </summary>
        None,

        /// <summary>
        /// Opens or closes the repository: the required secrets must be resolved
        /// otherwise an exception will be thrown.
        /// </summary>
        OpenRepository,

        /// <summary>
        /// Same as <see cref="OpenRepository"/> with a pull from the remote.
        /// </summary>
        OpenAndPullRepository
    }
}
