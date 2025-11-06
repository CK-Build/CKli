using CK.Core;
using System.Collections.Generic;

namespace CKli.Core;

public sealed class IssueEvent : WorldEvent
{
    readonly IReadOnlyList<Repo> _repos;
    readonly List<World.Issue> _issues;

    public IssueEvent( IActivityMonitor monitor,
                       World world,
                       IReadOnlyList<Repo> repos,
                       List<World.Issue> issues )
        : base( monitor, world )
    {
        _repos = repos;
        _issues = issues;
    }

    /// <summary>
    /// Gets the set of <see cref="Repo"/> from which issues should be detected.
    /// </summary>
    public IReadOnlyList<Repo> Repos => _repos;

    /// <summary>
    /// Adds a detected issue.
    /// </summary>
    /// <param name="issue">The issue.</param>
    public void Add( World.Issue issue )
    {
        Throw.CheckNotNullArgument( issue );
        _issues.Add( issue );
    }
}
