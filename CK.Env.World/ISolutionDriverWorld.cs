using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// View of the World from a Solution driver.
    /// </summary>
    public interface ISolutionDriverWorld
    {
        /// <summary>
        /// Gets the global work status.
        /// Null when the world is not initialized.
        /// </summary>
        GlobalWorkStatus? WorkStatus { get; }

        /// <summary>
        /// Registers a new driver.
        /// </summary>
        /// <param name="driver">The driver.</param>
        void Register( ISolutionDriver driver );

        /// <summary>
        /// Unregister a previously registered driver.
        /// </summary>
        /// <param name="driver">The driver.</param>
        void Unregister( ISolutionDriver driver );
    }
}
