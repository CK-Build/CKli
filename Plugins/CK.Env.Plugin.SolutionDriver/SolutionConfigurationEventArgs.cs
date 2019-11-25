using CK.Core;
using CK.Env.DependencyModel;
using System;
using System.Collections.Generic;

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
        /// <param name="buildSecrets">See <see cref="BuildRequiredSecrets"/>.</param>
        public SolutionConfigurationEventArgs(
            IActivityMonitor m,
            Solution solution,
            bool isNew,
            SolutionSpec spec,
            IList<(string SecretKeyName, string Secret)> buildSecrets )
            : base( m )
        {
            Solution = solution ?? throw new ArgumentNullException( nameof( solution ) );
            SolutionSpec = spec ?? throw new ArgumentNullException( nameof( spec ) );
            BuildRequiredSecrets = buildSecrets ?? throw new ArgumentNullException( nameof( SolutionSpec ) );
            IsNewSolution = isNew;
        }

        /// <summary>
        /// Gets a mutable list of secrets that are required for the build: these are the secrets that must be
        /// injected into the CodeCakeBuilderKeyVault.txt file.
        /// There is no guaranty that all secrets here are not null: missing secrets can exist (only the SecretKeyName is non null).
        /// </summary>
        public IList<(string SecretKeyName, string Secret)> BuildRequiredSecrets { get; }

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
                : (String.IsNullOrWhiteSpace( message ) ? "(no message)" : message);
        }

    }
}
