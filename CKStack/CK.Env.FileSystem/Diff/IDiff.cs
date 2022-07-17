using CK.Core;

namespace CK.Env.Diff
{
    interface IDiff
    {
        bool SendToBuilder( IActivityMonitor m, DiffRootResultBuilderBase diffRootResultBuilder );
    }
}
