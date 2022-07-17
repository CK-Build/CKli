using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Describes how multiples commands must be executed.
    /// </summary>
    public enum ParallelCommandMode
    {
        /// <summary>
        /// Commands are executed one by one.
        /// This is the default.
        /// </summary>
        Sequential,

        /// <summary>
        /// Multiple commands are executed simultaneously.
        /// </summary>
        Parallel,

        /// <summary>
        /// The user can choose whether to execute multiple commands one by one (<see cref="Sequential"/>)
        /// or in <see cref="Parallel"/>.
        /// </summary>
        UserChoice
    }
}
