using System;
using CK.Core;

namespace CK.Env.Analysis
{
    public interface IIssueBuilder
    {
        /// <summary>
        /// Gets the monitor to use.
        /// </summary>
        IActivityMonitor Monitor { get; }

        /// <summary>
        /// Creates an issue. Its description will be the captured log entries of the <see cref="Monitor"/>.
        /// If more than one issue is created they will all share the same description.
        /// </summary>
        /// <param name="identifier">Unique identifier of the issue.</param>
        /// <param name="title">Required title of the issue.</param>
        /// <param name="autoFix">Optional autmatix fix for the issue.</param>
        void CreateIssue( string identifier, string title, Func<IActivityMonitor, bool> autoFix = null );
    }
}
