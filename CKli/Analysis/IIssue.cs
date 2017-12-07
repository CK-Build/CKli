using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Analysis
{
    public interface IIssue
    {
        int Number { get; }

        LogLevel MaxLevel { get; }

        string Title { get; }

        string Description { get; }

        bool HasAutoFix { get; }

        bool AutoFix( IActivityMonitor monitor );

    }
}
