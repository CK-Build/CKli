using System;
using CK.Core;

namespace CK.Env.Analysis
{
    public interface IIssueBuilder
    {
        IActivityMonitor Monitor { get; }

        void CreateIssue( string identifier, string title = null, Func<IActivityMonitor, bool> autoFix = null );
    }
}
