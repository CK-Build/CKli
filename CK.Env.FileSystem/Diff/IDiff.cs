using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Diff
{
    interface IDiff
    {
        bool SendToBuilder( IActivityMonitor m, DiffRootResultBuilderBase diffRootResultBuilder );
    }
}
