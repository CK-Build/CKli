using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Provides a safe, read only, access to the current world state. 
    /// </summary>
    public interface IWorldState
    {
        /// <summary>
        /// Gets the world name.
        /// </summary>
        IWorldName WorldName { get; }

        /// <summary>
        /// Gets the current git status that applies to the whole world.
        /// </summary>
        StandardGitStatus GlobalGitStatus { get; }

        /// <summary>
        /// Gets the current global status.
        /// </summary>
        GlobalWorkStatus WorkStatus { get; }

        /// <summary>
        /// Gets the operation name (when <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.OtherOperation"/>).
        /// </summary>
        string OtherOperationName { get; }

        /// <summary>
        /// Gets the read only <see cref="XElement"/> general state.
        /// This is where state information that are not specific to an operation are stored.
        /// This element is read only: any attempt to modifiy it will throw an <see cref="InvalidOperationException"/>.
        /// </summary>
        XElement GeneralState { get; }

        /// <summary>
        /// Gets the read only <see cref="XElement"/> state for an operation.
        /// This element is read only: any attempt to modifiy it will throw an <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <param name="status">The work status.</param>
        /// <returns>The state element.</returns>
        XElement GetWorkState( GlobalWorkStatus status );

    }
}
