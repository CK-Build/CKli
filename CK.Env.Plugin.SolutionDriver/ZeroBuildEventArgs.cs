using CK.Core;
using CK.Env.DependencyModel;

namespace CK.Env.Plugin
{
    /// <summary>
    /// Fired before and after a build by <see cref="ISolutionDriver.ZeroBuildProject"/>.
    /// </summary>
    public class ZeroBuildEventArgs : EventMonitoredArgs
    {
        /// <summary>
        /// Initializes a new event instance.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="starting">Whether the build is strating or has been executed.</param>
        /// <param name="info">The build info.</param>
        public ZeroBuildEventArgs( IActivityMonitor m, bool starting, ZeroBuildProjectInfo info )
            : base( m )
        {
            IsStarting = starting;
            Info = info;
        }

        /// <summary>
        /// Gets whether the build is starting or has been executed.
        /// </summary>
        public bool IsStarting { get; }

        /// <summary>
        /// Gets the <see cref="ZeroBuildProjectInfo"/>.
        /// </summary>
        public ZeroBuildProjectInfo Info { get; }
    }
}
