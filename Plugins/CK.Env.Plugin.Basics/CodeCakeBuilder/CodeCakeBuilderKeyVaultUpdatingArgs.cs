using CK.Core;
using CK.Env.DependencyModel;
using System;
using System.Collections.Generic;

namespace CK.Env.Plugin
{
    /// <summary>
    /// Fired by <see cref="CodeCakeBuilderKeyVaultFile.Updating"/>.
    /// </summary>
    public class CodeCakeBuilderKeyVaultUpdatingArgs : EventMonitoredArgs
    {
        /// <summary>
        /// Initializes a new <see cref="CodeCakeBuilderKeyVaultUpdatingArgs"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="spec">Specification of the current solution.</param>
        /// <param name="solution">Current solution.</param>
        /// <param name="secrets">The secrets ready to be written.</param>
        public CodeCakeBuilderKeyVaultUpdatingArgs( IActivityMonitor m, SolutionSpec spec, ISolution solution, IDictionary<string,string> secrets )
            : base( m )
        {
            SolutionSpec = spec;
            Solution = solution;
            Secrets = secrets ?? throw new ArgumentNullException( nameof( secrets ) );
        }

        /// <summary>
        /// Gets the secrets that are about to be written to the CodeCakeBuilder key vault file.
        /// </summary>
        public IDictionary<string,string> Secrets { get; }

        /// <summary>
        /// Gets the specfication of the solution being updated.
        /// </summary>
        public ISolution Solution { get; }

        /// <summary>
        /// Gets the solution being updated.
        /// </summary>
        public SolutionSpec SolutionSpec { get; }

    }
}
