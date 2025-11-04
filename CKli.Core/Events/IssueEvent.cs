using CK.Core;
using System.Collections.Generic;

namespace CKli.Core;

public sealed class IssueEvent : WorldEvent
{
    readonly List<World.Issue> _issues;

    public IssueEvent( IActivityMonitor monitor,
                       World world,
                       List<World.Issue> issues )
        : base( monitor, world )
    {
        _issues = issues;
    }

    public void Add( World.Issue issue )
    {
        Throw.CheckNotNullArgument( issue );
        _issues.Add( issue );
    }
}
