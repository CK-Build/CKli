using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Analysis
{
    public interface IEnvAction
    {
        int Number { get; }

        string Title { get; }

        string Description { get; }

        bool Run( IActivityMonitor monitor );
    }
}
