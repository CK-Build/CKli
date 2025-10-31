using CK.Core;

namespace CKli.Core;

public sealed class IssueEvent : WorldEvent
{
    public IssueEvent( IActivityMonitor monitor, World world )
        : base( monitor, world )
    {
    }
}
