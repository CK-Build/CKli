using CK.Core;
using CK.Env.DependencyModel;
using System;
using System.Collections.Generic;
using System.Text;

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
        /// <param name="secrets">The secrets ready to be written.</param>
        public CodeCakeBuilderKeyVaultUpdatingArgs( IActivityMonitor m, ISolution solution, IDictionary<string,string> secrets )
            : base( m )
        {
            Solution = solution;
            Secrets = secrets ?? throw new ArgumentNullException( nameof( secrets ) );
        }

        /// <summary>
        /// Gets the secrets that are about to be written to the CodeCakeBuilder key vault file.
        /// </summary>
        public IDictionary<string,string> Secrets { get; }

        /// <summary>
        /// Gets the solution being updated.
        /// </summary>
        public ISolution Solution { get; }

    }
}
