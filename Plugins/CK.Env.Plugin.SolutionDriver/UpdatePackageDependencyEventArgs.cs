using CK.Core;
using System.Collections.Generic;

namespace CK.Env.Plugin
{
    /// <summary>
    /// Raised whenever package needs to be upgraded.
    /// </summary>
    public class UpdatePackageDependencyEventArgs : EventMonitoredArgs
    {
        /// <summary>
        /// Initializes a new <see cref="UpdatePackageDependencyEventArgs"/>.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="updateInfo">The update info to apply.</param>
        public UpdatePackageDependencyEventArgs( IActivityMonitor m, IReadOnlyCollection<UpdatePackageInfo> updateInfo )
            : base( m )
        {
            UpdateInfo = updateInfo;
        }

        /// <summary>
        /// Gets the update information.
        /// </summary>
        public IReadOnlyCollection<UpdatePackageInfo> UpdateInfo { get; }

    }
}
