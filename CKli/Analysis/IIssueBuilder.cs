using System;
using CK.Core;

namespace CK.Env.Analysis
{
    public interface IIssueBuilder
    {
        IActivityMonitor Monitor { get; }

        void CreateIssue( string title, Func<IActivityMonitor, bool> autoFix = null );
    }
}
