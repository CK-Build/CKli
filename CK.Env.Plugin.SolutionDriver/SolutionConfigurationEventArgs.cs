using CK.Core;
using CK.Env.DependencyModel;
using System;

namespace CK.Env.Plugin
{
    /// <summary>
    /// Raised whenever a solution has been configured so that any other
    /// plugins can participate to its configuration.
    /// </summary>
    public class SolutionConfigurationEventArgs : EventMonitoredArgs
    {

        /// <summary>
        /// Initializes a new <see cref="SolutionConfigurationEventArgs"/>.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="solution">The solution to be configured.</param>
        /// <param name="isNew">Whether the solution to be configured is a brand new one or is an existing one that is reconfigured.</param>
        /// <param name="spec">The solution specification.</param>
        public SolutionConfigurationEventArgs( IActivityMonitor m, Solution solution, bool isNew, SolutionSpec spec )
            : base( m )
        {
            Solution = solution;
        }

        /// <summary>
        /// Gets the solution that must be configured.
        /// </summary>
        public Solution Solution { get; }

        /// <summary>
        /// Gets whether the <see cref="Solution"/> is a brand new one or is an existing one that is reconfigured.
        /// </summary>
        public bool IsNewSolution { get; }

        /// <summary>
        /// Gets the solution specification.
        /// </summary>
        public SolutionSpec SolutionSpec { get; }

        /// <summary>
        /// Gets the failure messages.
        /// </summary>
        public string FailureMessage { get; private set; }


        /// <summary>
        /// See <see cref="PreventSolutionUse"/>.
        /// </summary>
        public bool ConfigurationFailed => FailureMessage != null;

        /// <summary>
        /// Sets <see cref="ConfigurationFailed"/> to true.
        /// Plugins can call this to prevent the solution to be used.
        /// The currently configured <see cref="Solution"/> will eventually
        /// be discarded.
        /// </summary>
        /// <param name="message">Failure message.</param>
        public void PreventSolutionUse( string message )
        {
            FailureMessage += message != null
                ? Environment.NewLine + message
                : (String.IsNullOrWhiteSpace( message ) ? "(no message)" : message );
        }
    }
}
