using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;

namespace CK.Env
{
    /// <summary>
    /// Raised whenever package needs to be upgraded.
    /// </summary>
    public class RunCommandEventArgs : EventMonitoredArgs
    {
        /// <summary>
        /// Initializes a new <see cref="RunCommandEventArgs"/>.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="updateInfo">The mutable start info.</param>
        public RunCommandEventArgs( IActivityMonitor m, ProcessStartInfo info )
            : base( m )
        {
            StartInfo = info;
        }

        /// <summary>
        /// Gets the start information.
        /// </summary>
        public ProcessStartInfo StartInfo { get; }

        /// <summary>
        /// Gets or sets the log level used to log the standard error stream of the process.
        /// Defaults to <see cref="LogLevel.Warn"/>.
        /// </summary>
        public LogLevel StdErrorLevel { get; set; } = Core.LogLevel.Warn;

    }
}
